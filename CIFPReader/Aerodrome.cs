using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIFPReader;

#pragma warning disable IDE0059

[JsonConverter(typeof(AerodromeJsonSerializer))]
public abstract record Aerodrome(string Client, string Header,
	string Identifier, string IATACode, ICoordinate Location, decimal MagneticVariation, AltitudeMSL Elevation,
	Altitude TransitionAlt, FlightLevel TransitionFL, Aerodrome.AirportUsage Usage, string Name,
	int FileRecordNumber, int Cycle) : RecordLine(Client, Header, FileRecordNumber, Cycle)
{

	public enum AirportUsage
	{
		Public = 'C',
		Military = 'M',
		Private = 'P'
	}

	public static new RecordLine? Parse(string line) =>
		(line[4], line[12]) switch {
			('P', 'A') => Airport.Parse(line),
			('P', 'G') => Runway.Parse(line),
			('H', 'A') => Heliport.Parse(line),
			('P' or 'H', 'D' or 'E' or 'F') => ProcedureLine.Parse(line),
			('P' or 'H', 'C') => Waypoint.Parse(line),
			('P', 'P') => PathPoint.Parse(line),
			('P' or 'H', 'S') => AirportMSA.Parse(line),
			('P', 'I') => ILS.Parse(line),
			('P', _) when line[5] == 'N' => NDB.Parse(line),

			_ => null
		};

	public class AerodromeJsonSerializer : JsonConverter<Aerodrome>
	{
		public override Aerodrome? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			string strVal = reader.GetString() ?? throw new JsonException();
			RecordLine? baseCall = JsonSerializer.Deserialize<RecordLine>(strVal, options);

			return baseCall?.Header switch {
				"PA" => JsonSerializer.Deserialize<Airport>(strVal, options),
				"HA" => JsonSerializer.Deserialize<Heliport>(strVal, options),

				_ => throw new JsonException()
			};
		}

		public override void Write(Utf8JsonWriter writer, Aerodrome value, JsonSerializerOptions options)
		{
			switch (value)
			{
				case Airport a:
					writer.WriteStringValue(JsonSerializer.Serialize(a));
					break;

				case Heliport h:
					writer.WriteStringValue(JsonSerializer.Serialize(h));
					break;

				default:
					throw new JsonException();
			}
		}
	}
}

public record Airport(string Client,
	string Identifier, string IATACode, uint MaxRunwayLength, bool IFR, ICoordinate Location, decimal MagneticVariation, AltitudeMSL Elevation,
	Altitude TransitionAlt, FlightLevel TransitionFL, Aerodrome.AirportUsage Usage, string Name,
	int FileRecordNumber, int Cycle) : Aerodrome(Client, "PA", Identifier, IATACode, Location, MagneticVariation, Elevation,
												 TransitionAlt, TransitionFL, Usage, Name, FileRecordNumber, Cycle)
{
	public static new Airport Parse(string line)
	{
		Check(line, 0, 1, "S");
		Check(line, 4, 6, "P ");
		Check(line, 12, 13, "A");

		string client = line[1..4];
		string identifier = line[6..10].Trim();
		string iataCode = line[13..16];

		uint maxRwyLen = uint.Parse(line[27..30] + "00");
		bool ifr = line[30] == 'Y';
		char longRwy = line[31];
		Coordinate location = new(line[32..51]);
		decimal magVar = decimal.Parse(line[52..56]) / 10 * (line[51] == 'E' ? -1 : 1);
		AltitudeMSL elevation = new(int.Parse(line[56..61]));
		AltitudeMSL transitionAlt = new(string.IsNullOrWhiteSpace(line[70..75]) ? 18000 : int.Parse(line[70..75]));
		FlightLevel transitionLevel = new(string.IsNullOrWhiteSpace(line[70..75]) ? 180 : (int.Parse(line[75..80]) / 100));
		AirportUsage usage = (AirportUsage)line[80];

		string name = line[93..123];
		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, iataCode, maxRwyLen, ifr, location, magVar, elevation, transitionAlt, transitionLevel, usage, name, frn, cycle);
	}
}

public record Runway(string Client,
	string Airport, string Identifier, uint Length, uint Width, Course Course, ICoordinate Endpoint, Altitude TDZE, Altitude TCH,
	string? Approach, Runway.RunwayApproachCategory ApproachCategory,
	int FileRecordNumber, int Cycle) : RecordLine(Client, "PG", FileRecordNumber, Cycle)
{
	[JsonIgnore]
	public string OppositeIdentifier =>
		Identifier switch {
			"N" => "S",
			"NE" => "SW",
			"NW" => "SE",
			"S" => "N",
			"SW" => "NE",
			"SE" => "NW",
			"E" => "W",
			"W" => "E",
			_ when Identifier.Any(char.IsDigit) =>
				((uint.Parse(new string(Identifier.TakeWhile(char.IsDigit).ToArray())) + 17) % 36 + 1)
			  + (Identifier.Last() switch { 'L' => "R", 'R' => "L", 'C' => "C", _ => "" }),
			_ => throw new Exception()
		};

	public static new Runway Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "P ");

		string airport = line[6..10].Trim();
		string icaoRegion = line[10..12];

		Check(line, 12, 13, "G");

		string runway = line[13..18].TrimEnd();
		bool waterway = !runway.StartsWith("RW");

		if (waterway)
			runway = "RW" + runway;

		Check(runway, 0, 2, "RW");

		CheckEmpty(line, 18, 21);
		Check(line, 21, 22, "0");

		uint length = uint.Parse(line[22..27]);
		MagneticCourse course = (waterway && string.IsNullOrWhiteSpace(line[27..31])) ? new(0m, null) : new(decimal.Parse(line[27..31]) / 10, null);

		CheckEmpty(line, 31, 32);

		Coordinate position = new(line[32..51]);

		CheckEmpty(line, 51, 60);

		decimal? heightAboveEllipsoid_meters = string.IsNullOrWhiteSpace(line[60..66]) ? null : decimal.Parse(line[60..66]) / 10;
		decimal? heightAboveEllipsoid_feet = heightAboveEllipsoid_meters is null ? null : heightAboveEllipsoid_meters * 3.28084m;

		AltitudeAGL tdze = new(0, int.Parse(line[66..71]));
		uint thresholdDisplacement = uint.Parse(line[71..75]);

		AltitudeAGL thresholdCrossingHeight = new(int.Parse(line[75..77]), tdze.GroundElevation);

		uint width = uint.Parse(line[77..80]);

		_ = line[80]; // ??? Appears to be I for ILS, but can't figure out the others

		string? approachIdent = string.IsNullOrWhiteSpace(line[81..85]) ? null : line[81..85].TrimEnd();
		RunwayApproachCategory approachCategory = (RunwayApproachCategory)line[85];

		CheckEmpty(line, 86, 90);
		string? secondApproachIdent = string.IsNullOrWhiteSpace(line[90..94]) ? null : line[90..94].TrimEnd();
		RunwayApproachCategory secondApproachCategory = (RunwayApproachCategory)line[94];
		CheckEmpty(line, 95, 101);

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, airport, runway[2..], length, width, course, position, tdze, thresholdCrossingHeight,
				   approachIdent, approachCategory, frn, cycle);
	}

	public enum RunwayApproachCategory
	{
		Localizer = '0',
		CatI = '1',
		CatII = '2',
		CatIII = '3',
		IGS = 'I',
		LDAWithGlideslope = 'L',
		LDANoGlideslope = 'A',
		SDFWithGlideslope = 'S',
		SDFNoGlideslope = 'F',
		NoApproach = ' '
	}
}

public record Heliport(string Client,
	string Identifier, string IATACode, string PadIdentifier, bool IFR, ICoordinate Location, decimal MagneticVariation, AltitudeMSL Elevation,
	Altitude TransitionAlt, FlightLevel TransitionFL, Aerodrome.AirportUsage Usage, string Name,
	int FileRecordNumber, int Cycle) : Aerodrome(Client, "HA", Identifier, IATACode, Location, MagneticVariation, Elevation,
												 TransitionAlt, TransitionFL, Usage, Name, FileRecordNumber, Cycle)
{
	public static new Heliport Parse(string line)
	{
		Check(line, 0, 1, "S");
		Check(line, 4, 5, "H");
		Check(line, 12, 13, "A");

		string client = line[1..4];
		string identifier = line[6..10];
		string iataCode = line[13..16];

		string padId = line[16..21];
		bool ifr = line[30] == 'Y';
		Coordinate location = new(line[32..51]);
		decimal magVar = decimal.Parse(line[52..56]) / 10 * (line[51] == 'E' ? -1 : 1);
		AltitudeMSL elevation = new(int.Parse(line[56..61]));
		AltitudeMSL transitionAlt = new(string.IsNullOrWhiteSpace(line[70..75]) ? 18000 : int.Parse(line[70..75]));
		FlightLevel transitionLevel = new(string.IsNullOrWhiteSpace(line[70..75]) ? 180 : (int.Parse(line[75..80]) / 100));
		AirportUsage usage = (AirportUsage)line[80];

		string name = line[93..123];
		int fileRecordNum = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, iataCode, padId, ifr, location, magVar, elevation, transitionAlt, transitionLevel, usage, name, fileRecordNum, cycle);
	}
}
