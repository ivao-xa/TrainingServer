using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIFPReader;

#pragma warning disable IDE0059

[JsonConverter(typeof(NavaidJsonSerializer))]
public abstract record Navaid(string Client, string Header,
	string Identifier, Coordinate Position, decimal? MagneticVariation, string Name,
	int FileRecordNumber, int Cycle) : RecordLine(Client, Header, FileRecordNumber, Cycle)
{
	public static new Navaid? Parse(string line) =>
		line[5] switch
		{
			' ' when line[28] == 'I' => NavaidILS.Parse(line),
			' ' when line[27] == 'V' => VOR.Parse(line),
			' ' => DME.Parse(line),

			'B' => NDB.Parse(line),

			_ => null
		};

	public enum ClassFacility
	{
		VOR = 'V',
		NDB = 'H',
		TACAN = ' ',
		SABH = 'S',
		MarineBacon = 'M'
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
		"CA1069:Enums values should not be duplicated",
		Justification = "Markers and navaids share letters.")]
	public enum ClassMarker
	{
		Inner = 'I',
		Middle = 'M',
		Outer = 'O',
		ILS = 'I',
		Back = 'C',
		TACAN = 'T',
		MilitaryTACAN = 'M',
		DME = 'D',
		None = ' '
	}

	public enum ClassPower
	{
		High = 'H',     // 200 watts + (min 75 nmi)
		Fifty = ' ',    // 50 - 199 watts (min 50 nmi)
		Medium = 'M',   // 25 - 49 watts (min 25 nmi)
		Low = 'L',       // 24 watts - (min 15 nmi)
		Terminal = 'T',
		ILS_TACAN = 'C',
		Undefined = 'U'
	}

	public enum ClassVoice
	{
		ATWB = 'A',
		ScheduledWeather = 'B',
		NoVoice = 'W',
		Voice = ' '
	}

	public class NavaidJsonSerializer : JsonConverter<Navaid>
	{
		public override Navaid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			string strVal = reader.GetString() ?? throw new JsonException();
			RecordLine? baseCall = JsonSerializer.Deserialize<RecordLine>(strVal, options);

			return baseCall?.Header switch
			{
				"DB" => JsonSerializer.Deserialize<NDB>(strVal, options),
				"DV" => JsonSerializer.Deserialize<VOR>(strVal, options),
				"DI" => JsonSerializer.Deserialize<NavaidILS>(strVal, options),
				"DD" => JsonSerializer.Deserialize<DME>(strVal, options),
				"PI" => JsonSerializer.Deserialize<ILS>(strVal, options),

				_ => throw new JsonException()
			};
		}

		public override void Write(Utf8JsonWriter writer, Navaid value, JsonSerializerOptions options)
		{
			switch (value)
			{
				case NDB n:
					writer.WriteStringValue(JsonSerializer.Serialize(n));
					break;

				case VOR v:
					writer.WriteStringValue(JsonSerializer.Serialize(v));
					break;

				case NavaidILS ni:
					writer.WriteStringValue(JsonSerializer.Serialize(ni));
					break;

				case DME d:
					writer.WriteStringValue(JsonSerializer.Serialize(d));
					break;

				case ILS i:
					writer.WriteStringValue(JsonSerializer.Serialize(i));
					break;

				default:
					throw new JsonException();
			}
		}
	}
}

public record NDB(string Client,
	string Identifier, ushort Channel, (Navaid.ClassFacility Facility, Navaid.ClassMarker Marker, Navaid.ClassPower Power, Navaid.ClassVoice Voice) Class,
	Coordinate Position, decimal? MagneticVariation, string Name,
	int FileRecordNumber, int Cycle) : Navaid(Client, "DB", Identifier, Position, MagneticVariation, Name, FileRecordNumber, Cycle)
{
	public static new NDB Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];

		if (line[4..6] != "PN")
			Check(line, 4, 6, "DB");

		string airport = line[6..10];
		string airportIcaoRegion = line[10..12];
		CheckEmpty(line, 12, 13);
		string identifier = line[13..17].TrimEnd();
		CheckEmpty(line, 17, 19);
		string icaoRegion = line[19..21];
		Check(line, 21, 22, "0");
		Check(line, 22, 23, "0");
		ushort channel = ushort.Parse(line[23..26]);
		Check(line, 26, 27, "0");

		(ClassFacility Facility, ClassMarker Marker, ClassPower Power, ClassVoice Voice) @class =
			((ClassFacility)line[27], (ClassMarker)line[28], (ClassPower)line[29], (ClassVoice)line[30]);
		CheckEmpty(line, 31, 32); // No BFO collocation in the CIFPs

		Coordinate position = new(line[32..51]);

		CheckEmpty(line, 51, 74);

		decimal magVar = decimal.Parse(line[75..79]) / 10;
		magVar *= line[74] == 'E' ? -1 : 1;

		CheckEmpty(line, 79, 90);
		Check(line, 90, 93, "NAR"); // Spheroid WGS-84
		
		string name = line[93..123].TrimEnd();

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, channel, @class, position, magVar, name, frn, cycle);
	}
}

public record VOR(string Client,
	string Identifier, decimal Frequency, (Navaid.ClassFacility Facility, Navaid.ClassMarker Marker, Navaid.ClassPower Power, Navaid.ClassVoice Voice) Class,
	Coordinate Position, decimal? MagneticVariation, AltitudeMSL Elevation, string Name, DME? CollocatedDME,
	int FileRecordNumber, int Cycle) : Navaid(Client, "DV", Identifier, Position, MagneticVariation, Name, FileRecordNumber, Cycle)
{
	public static new VOR Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 13, "D        ");
		string identifier = line[13..17].TrimEnd();
		Check(line, 17, 19, "  ");
		string icaoRegion = line[19..21];
		Check(line, 21, 22, "0");
		decimal frequency = decimal.Parse(line[22..27]) / 10;

		(ClassFacility Facility, ClassMarker Marker, ClassPower Power, ClassVoice Voice) @class =
			((ClassFacility)line[27], (ClassMarker)line[28], (ClassPower)line[29], (ClassVoice)line[30]);
		Check(line, 31, 32, " "); // No BFO collocation in the CIFPs

		if (@class.Facility != ClassFacility.VOR)
			throw new ArgumentException("Provided input string is not a VOR");

		Coordinate position = new(line[32..51]);

		DME? collocatedDME = null;

		if (@class.Marker == ClassMarker.DME || @class.Marker == ClassMarker.TACAN)
		{
			if (!string.IsNullOrWhiteSpace(line[51..55]))
				Check(line, 51, 55, identifier.PadRight(4, ' ')); // Does the DME identifier match the VOR identifier?

			collocatedDME = DME.Parse(line);
		}
		else
			CheckEmpty(line, 51, 74);

		decimal magVar = decimal.Parse(line[75..79]) / 10;
		magVar *= line[74] == 'E' ? -1 : 1;

		AltitudeMSL elevation = new((int)(decimal.Parse(line[79..85]) / 10));

		CheckEmpty(line, 85, 90);
		Check(line, 90, 93, "NAR"); // Spheroid WGS-84

		string name = line[93..123].TrimEnd();

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, frequency, @class, position, magVar, elevation, name, collocatedDME, frn, cycle);
	}
}

public record NavaidILS(string Client,
	string Identifier, decimal Frequency, (Navaid.ClassFacility Facility, Navaid.ClassMarker Marker, Navaid.ClassPower Power, Navaid.ClassVoice Voice, bool OnField) Class,
	Coordinate Position, decimal? MagneticVariation, AltitudeMSL Elevation, string Name, DME CollocatedDME,
	int FileRecordNumber, int Cycle) : Navaid(Client, "DI", Identifier, Position, MagneticVariation, Name, FileRecordNumber, Cycle)
{
	public static new NavaidILS Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "D ");
		string airport = line[6..10];
		string airportRegion = line[10..12];

		string identifier = line[13..17].TrimEnd();
		CheckEmpty(line, 17, 19);
		string icaoRegion = line[19..21];
		Check(line, 21, 22, "0");
		decimal frequency = decimal.Parse(line[22..27]) / 10;

		(ClassFacility Facility, ClassMarker Marker, ClassPower Power, ClassVoice Voice, bool OnField) @class =
			((ClassFacility)line[27], (ClassMarker)line[28], (ClassPower)line[29], (ClassVoice)line[30], line[31] == ' ');

		if (@class.Marker != ClassMarker.Inner)
			throw new ArgumentException("Provided input string is not an ILS");

		if (!string.IsNullOrWhiteSpace(line[51..55]))
			Check(line, 51, 55, identifier.PadRight(4, ' ')); // Does the DME identifier match the VOR identifier?

		DME collocatedDME = DME.Parse(line);

		decimal magVar = decimal.Parse(line[75..79]) / 10;
		magVar *= line[74] == 'E' ? -1 : 1;

		AltitudeMSL elevation = new((int)(decimal.Parse(line[79..85]) / 10));

		CheckEmpty(line, 85, 90);
		Check(line, 90, 93, "NAR"); // Spheroid WGS-84

		string name = line[93..123].TrimEnd();

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, frequency, @class, collocatedDME.Position, magVar, elevation, name, collocatedDME, frn, cycle);
	}
}

public record DME(string Client,
	string Identifier, ushort Channel, (Navaid.ClassFacility Facility, Navaid.ClassMarker Marker, Navaid.ClassPower Power, Navaid.ClassVoice Voice) Class,
	Coordinate Position, AltitudeMSL Elevation, string Name,
	int FileRecordNumber, int Cycle) : Navaid(Client, "DD", Identifier, Position, null, Name, FileRecordNumber, Cycle)
{
	public static new DME Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "D ");
		CheckEmpty(line, 17, 19);
		string icaoRegion = line[19..21];
		Check(line, 21, 22, "0");
		decimal frequency = decimal.Parse(line[22..27]) / 10;
		if (frequency >= 1000)
			frequency /= 10;

		ushort channel = (ushort)((int)(frequency * 10) switch
		{
			>= 1344 and < 1360 => (int)(frequency * 10) - 1344 + 1,
			>= 1080 and < 1123 => (int)(frequency * 10) - 1080 + 17,
			>= 1333 and < 1343 => (int)(frequency * 10) - 1333 + 60,
			>= 1123 and < 1180 => (int)(frequency * 10) - 1123 + 70,

			_ => throw new NotImplementedException()
		});

		(ClassFacility Facility, ClassMarker Marker, ClassPower Power, ClassVoice Voice) @class =
			((ClassFacility)line[27], (ClassMarker)line[28], (ClassPower)line[29], (ClassVoice)line[30]);

		bool bfoCollocated = line[31] != ' ';

		if (!new ClassMarker[] { ClassMarker.DME, ClassMarker.TACAN, ClassMarker.MilitaryTACAN, ClassMarker.ILS }.Contains(@class.Marker))
			throw new ArgumentException("Provided input does not have DME");

		string identifier = line[51..55].TrimEnd();
		Coordinate position = new(line[55..74]);

		AltitudeMSL elevation = new((int)(decimal.Parse(line[79..85]) / 10));

		CheckEmpty(line, 85, 90);
		Check(line, 90, 93, "NAR"); // Spheroid WGS-84

		string name = line[93..123].TrimEnd();

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, channel, @class, position, elevation, name, frn, cycle);
	}
}

public record ILS(string Client,
	string Airport, string Identifier, Runway.RunwayApproachCategory Category, decimal Frequency, string Runway,
	Coordinate LocalizerPosition, MagneticCourse LocalizerCourse, Coordinate? GlideslopePosition,
	int FileRecordNumber, int Cycle) : Navaid(Client, "PI", Identifier, LocalizerPosition, LocalizerCourse.Variation, $"{Identifier} ({Airport} - {Runway})", FileRecordNumber, Cycle)
{
	public static new ILS Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "P ");

		string airport = line[6..10];
		string icaoRegion = line[10..12];
		Check(line, 12, 13, "I");
		string identifier = line[13..17];
		Runway.RunwayApproachCategory category = (Runway.RunwayApproachCategory)line[17];

		CheckEmpty(line, 18, 21);
		Check(line, 21, 22, "0");

		decimal frequency = decimal.Parse(line[22..27]) / 100;
		string runway = line[27..32];

		Coordinate localizerPosition = new(line[32..51]);
		decimal locMagCrs = decimal.Parse(line[51..55]) / 10;
		Coordinate? glideslopePosition = string.IsNullOrWhiteSpace(line[55..74]) ? null : new(line[55..74]);

		MagneticCourse localizerCourse = new(locMagCrs, decimal.Parse(line[91..95]) / 10 * (line[90] == 'E' ? -1 : 1));

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, airport, identifier, category, frequency, runway, localizerPosition, localizerCourse, glideslopePosition, frn, cycle);
	}
}