using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIFPReader;

#pragma warning disable IDE0059

internal static class EnrouteLine
{
	public static RecordLine? Parse(string line) =>
		line[5] switch
		{
			'A' => Waypoint.Parse(line),
			'R' => AirwayFixLine.Parse(line),

			_ => null
		};
}

public record Waypoint(string Client,
	string Identifier, string Airport, Waypoint.WPType Type, Waypoint.WPUsage Usage, Coordinate Position, string Name,
	int FileRecordNumber, int Cycle) : RecordLine(Client, "EA", FileRecordNumber, Cycle)
{
	public static new Waypoint Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		if (!"PH".Contains(line[4]))
		{
			Check(line, 4, 6, "EA");
			CheckEmpty(line, 10, 13); // ICAO region is given later, so not used in CIFPs
		}
		else
		{
			Check(line, 4, 6, "P ", "H ");
			string frontIcaoRegion = line[10..12];
			Check(line, 12, 13, "C");
		}
		string airport = line[6..10];

		string identifier = line[13..18].TrimEnd();
		CheckEmpty(line, 18, 19);
		string icaoRegion = line[19..21];
		Check(line, 21, 22, "0");
		CheckEmpty(line, 22, 26);

		WPType type =
			line[26] == ' '
			? (WPType)char.ToLower(line[27])
			: (WPType)line[26];
		CheckEmpty(line, 28, 30);
		WPUsage usage = (WPUsage)line[30];
		CheckEmpty(line, 31, 32);

		Coordinate position = new(line[32..51]);
		CheckEmpty(line, 51, 74);

		decimal magVar = decimal.Parse(line[75..79]);
		magVar *= line[74] == 'E' ? -1 : 1;

		CheckEmpty(line, 79, 84);
		Check(line, 84, 87, "NAR"); // WGS-84 spheroid
		CheckEmpty(line, 87, 98);

		string name = line[98..123];
		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, airport, type, usage, position, name, frn, cycle);
	}

	public enum WPType
	{
		CombinedNamedIntersectionAndRNAV = 'C',
		NamedIntersection = 'R',
		VFRWP = 'V',
		RNAVWP = 'W',

		// ^ caps for first char, v lowercase for second char

		LatLonFullDegree = 'v',
		LatLonHalfDegree = 'w',
	}

	public enum WPUsage
	{
		HighLow = 'B',
		High = 'H',
		Low = 'L',
		Terminal = ' '
	}
}

public record PathPoint(string Client,
	string Airport, string Approach, string Runway, Coordinate Position,
	int FileRecordNumber, int Cycle) : RecordLine(Client, "PP", FileRecordNumber, Cycle)
{
	public static new PathPoint? Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "P ");
		string airport = line[6..10];
		string icaoRegion = line[10..12];
		Check(line, 12, 13, "P");

		string approach = line[13..19].TrimEnd();
		string runway = line[19..24].TrimEnd();

		Check(line, 24, 26, "00");

		if (line[26] >= '2')
			return null;

		_ = line[27..37]; // Data IDs and other things I don't care about.

		Coordinate thresholdPoint = new(line[37..60]);

		decimal? heightAboveEllipsoid_meters = string.IsNullOrWhiteSpace(line[60..66]) ? null : decimal.Parse(line[60..66]) / 10;
		decimal? heightAboveEllipsoid_feet = heightAboveEllipsoid_meters is null ? null : heightAboveEllipsoid_meters * 3.28084m;

		decimal glidepathAngle = decimal.Parse(line[66..70]) / 100;

		Coordinate fpap = new(line[70..93]);

		decimal tch = decimal.Parse(line[102..108]) / 10;
		Check(line, 108, 109, "F");	// Feet. M would be metres.

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, airport, approach, runway, thresholdPoint, frn, cycle);
	}
}

[JsonConverter(typeof(AirwayJsonConverter))]
public class Airway : IEnumerable<Airway.AirwayFix>
{
	public string Identifier { get; init; }

	private readonly AirwayFix[] fixes;

	protected Airway(string identifier, AirwayFix[] fixes) =>
		(Identifier, this.fixes) = (identifier, fixes);

	public Airway(string identifier, AirwayFixLine[] fixLines, Dictionary<string, HashSet<ICoordinate>> fixDb)
	{
		if (fixLines.Length < 2)
			throw new ArgumentException("Airway must have at least two points.", nameof(fixLines));

		List<AirwayFix> fs = new() { new(fixLines.First(), fixDb, fixLines.First().Fix.Resolve(fixDb, fixLines[1].Fix).Position) };

		foreach (AirwayFixLine afl in fixLines.Skip(1))
			fs.Add(new(afl, fixDb, fs.Last().Point.GetCoordinate()));

		(Identifier, fixes) = (identifier, fs.ToArray());
	}

	public IEnumerator<AirwayFix> GetEnumerator() =>
		((IEnumerable<AirwayFix>)fixes).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() =>
		fixes.GetEnumerator();

	public record AirwayFix
	{
		public string? Name { get; init; }
		public ICoordinate Point { get; init; }
		public AltitudeRestriction InboundAltitude { get; init; }
		public AltitudeRestriction OutboundAltitude { get; init; }

		[JsonConstructor]
		public AirwayFix(string name, ICoordinate point, AltitudeRestriction inboundAltitude, AltitudeRestriction outboundAltitude) =>
			(Name, Point, InboundAltitude, OutboundAltitude) = (name, point, inboundAltitude, outboundAltitude);

		public AirwayFix(AirwayFixLine fix, Dictionary<string, HashSet<ICoordinate>> fixes, Coordinate reference)
		{
			Name = fix.Fix.Name;
			Point = fix.Fix.Resolve(fixes, reference);
			(InboundAltitude, OutboundAltitude) = (fix.InboundAltitude, fix.OutboundAltitude);
		}
	}

	public class AirwayJsonConverter : JsonConverter<Airway>
	{
		public override Airway? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
				throw new JsonException();

			List<AirwayFix> fixes = new();
			string? identifier = null;

			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndObject)
					break;

				string propName = reader.GetString() ?? throw new JsonException();
				reader.Read();
				switch (propName)
				{
					case "Identifier":
						identifier = reader.GetString() ?? throw new JsonException();
						break;

					case "Fixes":
						if (reader.TokenType != JsonTokenType.StartArray)
							throw new JsonException();

						while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
							fixes.Add(JsonSerializer.Deserialize<AirwayFix>(ref reader, options) ?? throw new JsonException());
						break;
				}
			}

			if (reader.TokenType != JsonTokenType.EndObject || identifier is null)
				throw new JsonException();

			return new(identifier, fixes.ToArray());
		}

		public override void Write(Utf8JsonWriter writer, Airway value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			writer.WriteString("Identifier", value.Identifier);
			writer.WritePropertyName("Fixes");
			writer.WriteStartArray();
			foreach (AirwayFix af in value)
				JsonSerializer.Serialize(writer, af, options);
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
	}
}

public record AirwayFixLine(string Client,
	string AirwayIdentifier, int SequenceNumber, UnresolvedWaypoint Fix, bool RNAV, AirwayFixLine.RTLevel Level, MagneticCourse? OutboundCourse,
	decimal? Distance, MagneticCourse? InboundCourse, AltitudeRestriction OutboundAltitude, AltitudeRestriction InboundAltitude,
	int FileRecordNumber, int Cycle) : RecordLine(Client, "ER", FileRecordNumber, Cycle)
{
	public static new AirwayFixLine Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "ER");
		CheckEmpty(line, 6, 13);

		string identifier = line[13..18].TrimEnd();
		CheckEmpty(line, 18, 25);

		int sequenceNum = int.Parse(line[25..29]);

		UnresolvedWaypoint fix = new(line[29..34].TrimEnd());
		string icaoRegion = line[34..36];

		if (!new[] { "D ", "DB", "EA" }.Contains(line[36..38]))
			Fail(36);

		Check(line, 38, 39, "0");

		_ = line[39..44]; // ??? No clue what this is. It's labelled as a 'DESC CODE'?

		if (!"RO".Contains(line[44]))
			Fail(44);
		bool rnav = line[44] == 'R';

		RTLevel level = (RTLevel)line[45];

		CheckEmpty(line, 46, 70);

		MagneticCourse? outboundCourse = string.IsNullOrWhiteSpace(line[70..74]) ? null : new(decimal.Parse(line[70..74]) / 10, null);
		decimal? distance = string.IsNullOrWhiteSpace(line[74..78]) ? null : decimal.Parse(line[74..78]) / 10;
		MagneticCourse? inboundCourse = string.IsNullOrWhiteSpace(line[78..82]) ? null : new(decimal.Parse(line[78..82]) / 10, null);

		CheckEmpty(line, 82, 83);

		static AltitudeMSL? getAlt(string data) =>
			string.IsNullOrWhiteSpace(data) || data == "UNKNN"
			? null
			: new(int.Parse(data));

		AltitudeMSL? outboundMinimumAlt = getAlt(line[83..88]);
		AltitudeMSL? inboundMinimumAlt = getAlt(line[88..93]);
		AltitudeMSL? maximumAlt = getAlt(line[93..98]);

		CheckEmpty(line, 98, 123);

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, identifier, sequenceNum, fix, rnav, level, outboundCourse, distance, inboundCourse, new(outboundMinimumAlt, maximumAlt),
			new(inboundMinimumAlt, maximumAlt), frn, cycle);
	}

	public static bool TryParse(string line, [NotNullWhen(true)] out AirwayFixLine? result)
	{
		try
		{
			result = Parse(line);
			return true;
		}
		catch
		{
			result = null;
			return false;
		}
	}

	public enum RTLevel
	{
		All = ' ',
		High = 'H',
		Low = 'L'
	}
}