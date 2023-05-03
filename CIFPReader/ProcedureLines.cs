using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using static CIFPReader.ProcedureLine;

namespace CIFPReader;

public abstract record ProcedureLine(string Client, string Header,
	string Airport, string Name,
	int FileRecordNumber, int Cycle) : RecordLine(Client, Header, FileRecordNumber, Cycle)
{
	public static new ProcedureLine? Parse(string line) =>
		line[12] switch {
			'D' => SIDLine.Parse(line),
			'E' => STARLine.Parse(line),
			'F' => ApproachLine.Parse(line),

			_ => null
		};

	public static (PathTermination, IProcedureEndpoint? ep, IProcedureVia? via) GetPathSegment(string line)
	{
		PathTermination pathTerm = PathTerminationFromString(line[47..49]);

		if (!new[] { ' ', 'Y' }.Contains(line[49]))
			CheckEmpty(line, 49, 50);

		string? receivedNavaid = line[50..54].Trim(), navaidIcaoRegion = null;
		if (string.IsNullOrWhiteSpace(receivedNavaid))
		{
			CheckEmpty(line, 54, 56);
			receivedNavaid = null;
		}
		else
			navaidIcaoRegion = line[54..56];

		[DebuggerStepThrough]
		static decimal? getDecimal(string data)
		{
			if (!string.IsNullOrWhiteSpace(data))
				return decimal.Parse(data);

			CheckEmpty(data, 0, data.Length);
			return null;
		}

		decimal? arcRadius = getDecimal(line[56..62]) / 1000;
		decimal? stationBearing = getDecimal(line[62..66]) / 10;
		decimal? stationDistance = getDecimal(line[66..70]) / 10;
		if (stationBearing == 0 && stationDistance == 0)
		{
			stationBearing = null;
			stationDistance = null;
		}

		decimal? magcourseRaw = getDecimal(line[70..74]) / 10;
		MagneticCourse? course = magcourseRaw is null ? null : new(magcourseRaw.Value, null);
		decimal? routeDistance = line[74] == 'T' ? null : getDecimal(line[74..78]) / 10;
		decimal? routeTime = line[74] == 'T' ? getDecimal(line[75..78]) / 10 : null;

		string fixType = line[78..80];
		CheckEmpty(line, 80, 82);

		string? arcOrigin = line[106..111].Trim() == "" ? null : line[106..111].Trim();
		string arcOriginIcaoRegion = line[112..114];
		string arcOriginFixType = line[114..116];

		IProcedureVia? via = null;
		IProcedureEndpoint? ep = null;

		course ??= stationBearing is not null ? new(stationBearing!.Value, null) : null;

		if (pathTerm.HasFlag(PathTermination.Hold))
		{
			if (course is null)
				throw new Exception("Tried to instantiate racetrack without course.");
			if (routeDistance is null && routeTime is null)
				throw new Exception("Tried to instantiate racetrack without leg distance or time.");

			via = new Racetrack(null, null, course, routeDistance, routeTime is null ? null : TimeSpan.FromMinutes((double)routeTime));
		}
		else if (course is not null && (pathTerm.HasFlag(PathTermination.Track) || pathTerm.HasFlag(PathTermination.Course) || pathTerm.HasFlag(PathTermination.Heading)))
			via = course;
		else if (pathTerm.HasFlag(PathTermination.ProcedureTurn) && course is not null)
			via = course;
		else if (pathTerm.HasFlag(PathTermination.Arc))
			via = new Arc(
				null,
				(arcOrigin ?? receivedNavaid) is null ? null : new((arcOrigin ?? receivedNavaid)!),
				stationDistance ?? arcRadius ?? throw new FormatException("Missing arc radius."),
				course ?? throw new FormatException("Missing arc endpoint.")
			);

		if (pathTerm.HasFlag(PathTermination.UntilDistance))
			ep = receivedNavaid is null ? new Distance(null, routeDistance!.Value) : new UnresolvedDistance(new(receivedNavaid), routeDistance!.Value);
		else if (pathTerm.HasFlag(PathTermination.UntilRadial))
			ep = receivedNavaid is null ? throw new Exception() : new UnresolvedRadial(new(receivedNavaid), new(stationBearing!.Value, null));

		return (pathTerm, ep, via);
	}

	protected static PathTermination PathTerminationFromString(string purpose) =>
		purpose switch {
			// => Termination						| Via
			"IF" => PathTermination.UntilCrossing | PathTermination.Direct,
			"TF" => PathTermination.UntilCrossing | PathTermination.Direct,
			"CF" => PathTermination.UntilCrossing | PathTermination.Course,
			"DF" => PathTermination.UntilCrossing | PathTermination.Direct,
			"FA" => PathTermination.UntilAltitude | PathTermination.Direct,
			"FC" => PathTermination.ForDistance | PathTermination.Track,
			"FD" => PathTermination.UntilDistance | PathTermination.Track,
			"FM" => PathTermination.UntilTerminated | PathTermination.Track,
			"CA" => PathTermination.UntilAltitude | PathTermination.Course,
			"CD" => PathTermination.UntilDistance | PathTermination.Course,
			"CI" => PathTermination.UntilIntercept | PathTermination.Course,
			"CR" => PathTermination.UntilRadial | PathTermination.Course,
			"RF" => PathTermination.ForDistance | PathTermination.Arc,
			"AF" => PathTermination.UntilCrossing | PathTermination.Arc,
			"VA" => PathTermination.UntilAltitude | PathTermination.Heading,
			"VD" => PathTermination.UntilDistance | PathTermination.Heading,
			"VI" => PathTermination.UntilIntercept | PathTermination.Heading,
			"VM" => PathTermination.UntilTerminated | PathTermination.Heading,
			"VR" => PathTermination.UntilRadial | PathTermination.Heading,

			"HA" => PathTermination.UntilAltitude | PathTermination.Hold,
			"HF" => PathTermination.UntilCrossing | PathTermination.Hold,
			"HM" => PathTermination.UntilTerminated | PathTermination.Hold,

			"PI" => PathTermination.UntilIntercept | PathTermination.ProcedureTurn,


			/* ******************************************** *
			 * IF: Initial Fix								*
			 * TF: Track to a Fix							*
			 * CF: Course to a Fix							*
			 * DF: Direct to a Fix							*
			 * FA: Fix to an Altitude						*
			 * FC: Track from a Fix for a Distance			*
			 * FD: Track from a Fix to a DME Distance		*
			 * FM: From a Fix to a Manual termination		*
			 * CA: Course to an Altitude					*
			 * CD: Course to a DME Distance					*
			 * CI: Course to an Intercept					*
			 * CR: Course to a Radial termination			*
			 * RF: Constant Radius Arc						*
			 * AF: Arc to Fix								*
			 * VA: Heading to an Altitude					*
			 * VD: Heading to a DME Distance termination	*
			 * VI: Heading to an Intercept					*
			 * VM: Heading to a Manual termination			*
			 * VR: Heading to a Radial termination			*
			 * PI: 045/180 Procedure turn					*
			 * HA/HF/HM: Hold in lieu of PT					*
			 * ******************************************** */

			_ => throw new NotImplementedException()
		};

	[Flags]
	public enum PathTermination
	{
		// Termination
		UntilCrossing = 0b_0000001,
		UntilAltitude = 0b_0000010,
		UntilDistance = 0b_0000100,
		UntilIntercept = 0b_0001000,
		UntilRadial = 0b_0010000,
		ForDistance = 0b_0100000,
		UntilTerminated = 0b_1000000,

		// Via
		Heading = 0b_000001_0000000,
		Track = 0b_000010_0000000,
		Course = 0b_000100_0000000,
		Arc = 0b_001000_0000000,
		ProcedureTurn = 0b_010000_0000000,
		Direct = 0b_100000_0000000,

		// Hold
		Hold = 0b_1_000000_0000000
	}
}

public record SIDLine(string Client,
	string Airport, string Name, SIDLine.SIDRouteType RouteType, string Transition,
	PathTermination FixInstruction, IProcedureEndpoint? Endpoint, IProcedureVia? Via,
	AltitudeRestriction AltitudeRestriction, SpeedRestriction SpeedRestriction,
	int FileRecordNumber, int Cycle) : ProcedureLine(Client, "PD", Airport, Name, FileRecordNumber, Cycle)
{
	public static new SIDLine Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];

		Check(line, 4, 6, "P ");
		string airport = line[6..10].Trim();
		string icaoRegion = line[10..12];

		Check(line, 12, 13, "D");
		string name = line[13..19].Trim();
		SIDRouteType routeType = (SIDRouteType)line[19];
		string transitionIdentifier = line[20..25].Trim();

		if (routeType == SIDRouteType.CommonRoute || routeType == SIDRouteType.CommonRoute_RNAV)
			if (line[20..22] != "RW" && !string.IsNullOrWhiteSpace(line[20..25]))
				Check(line, 20, 25, "ALL  ");

		CheckEmpty(line, 25, 26);
		int sequenceNum = int.Parse(line[26..29]);

		UnresolvedWaypoint? fix = line[29..34].Trim() == "" ? null : new(line[29..34].Trim());

		string fix_icaoRegion = line[34..36];

		string fixWPTerminalType = line[36..38];
		Check(line, 38, 39, "0");
		_ = line[39..43]; // ??? Here's that weird DESC CODE thing again. Still no clue how to read it.

		char turnDirection = line[43];

		if ("456".Contains((char)routeType))
		{
			int? rnav = string.IsNullOrWhiteSpace(line[44..46]) ? null : int.Parse(line[44..46]);
			bool overfly = line[47] == '1';
		}
		else
			CheckEmpty(line, 44, 47);

		(PathTermination pathTerm, IProcedureEndpoint? ep, IProcedureVia? via) = GetPathSegment(line);
		if (via is Racetrack r)
			via = r with { Waypoint = fix };

		static AltitudeMSL? getAlt(string data) =>
			string.IsNullOrWhiteSpace(data)
			? null
			: data[0..2] == "FL"
			  ? new FlightLevel(int.Parse(data[2..]))
			  : new(int.Parse(data));

		AltitudeRestriction altitudeRestriction =
			line[82] == ' '
			? AltitudeRestriction.Unrestricted
			: AltitudeRestriction.FromDescription(
				(AltitudeRestriction.AltitudeDescription)line[82],
				getAlt(line[84..89]),
				getAlt(line[89..94])
			  );

		AltitudeMSL? transitionAltitude = getAlt(line[94..99]);
		SpeedRestriction? speedRestriction =
			string.IsNullOrWhiteSpace(line[99..102])
			? SpeedRestriction.Unrestricted
			: new(null, uint.Parse(line[99..102]));

		CheckEmpty(line, 102, 106);
		// line[106..111] is part of the pathterm.
		Check(line, 111, 112, " ", "A"); // Can be 'A' sometimes for some reason? (see KJAC ALPIN4)
										 // line[112..116] is part of the pathterm.
		CheckEmpty(line, 116, 117);
		char speedLimit = line[117]; // ??? Looks like either '-' or ' ', not really sure why.
		CheckEmpty(line, 118, 123);

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, airport, name, routeType, transitionIdentifier, pathTerm, ep ?? fix,
			via, altitudeRestriction, speedRestriction, frn, cycle);
	}

	public static bool TryParse(string line, [NotNullWhen(true)] out SIDLine? result)
	{
		try
		{
			if (Parse(line) is SIDLine sl)
			{
				result = sl;
				return true;
			}
		}
		catch (FormatException) { }

		result = null;
		return false;
	}

	public enum SIDRouteType
	{
		RunwayTransition = '1',
		CommonRoute = '2',
		EnrouteTransition = '3',
		RunwayTransition_RNAV = '4',
		CommonRoute_RNAV = '5',
		EnrouteTransition_RNAV = '6',
		RunwayTransition_Vector = 'T',
		EnrouteTransition_Vector = 'V'
	}
}

public record STARLine(string Client,
	string Airport, string Name, STARLine.STARRouteType RouteType, string Transition, bool Initial,
	PathTermination FixInstruction, IProcedureEndpoint? Endpoint, IProcedureVia? Via,
	AltitudeRestriction AltitudeRestriction, SpeedRestriction SpeedRestriction,
	int FileRecordNumber, int Cycle) : ProcedureLine(Client, "PE", Airport, Name, FileRecordNumber, Cycle)
{
	public static new STARLine Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];

		Check(line, 4, 6, "P ");
		string airport = line[6..10].Trim();
		string icaoRegion = line[10..12];

		Check(line, 12, 13, "E");
		string name = line[13..19].Trim();
		STARRouteType routeType = (STARRouteType)line[19];
		string transitionIdentifier = line[20..25].Trim();

		if (routeType == STARRouteType.CommonRoute || routeType == STARRouteType.CommonRoute_RNAV)
			if (line[20..22] != "RW" && !string.IsNullOrWhiteSpace(line[20..25]))
				Check(line, 20, 25, "ALL  ");

		CheckEmpty(line, 25, 26);
		int sequenceNum = int.Parse(line[26..29]);

		UnresolvedWaypoint? fix = line[29..34].Trim() == "" ? null : new(line[29..34].Trim());
		string fix_icaoRegion = line[34..36];
		string fixWPTerminalType = line[36..38];

		Check(line, 38, 39, "0");
		_ = line[39..43]; // ??? Here's that weird DESC CODE thing again. Still no clue how to read it.

		char turnDirection = line[43];
		CheckEmpty(line, 44, 47);

		bool initialFix = line[47..49] == "IF";

		(PathTermination pathTerm, IProcedureEndpoint? ep, IProcedureVia? procVia) = GetPathSegment(line);
		if (procVia is Racetrack r)
			procVia = r with { Waypoint = fix };

		static AltitudeMSL? getAlt(string data) =>
			string.IsNullOrWhiteSpace(data)
			? null
			: data[0..2] == "FL" ? new FlightLevel(int.Parse(data[2..])) : new(int.Parse(data));


		AltitudeRestriction altitudeRestriction =
			line[82] == ' '
			? AltitudeRestriction.Unrestricted
			: AltitudeRestriction.FromDescription(
				(AltitudeRestriction.AltitudeDescription)line[82],
				getAlt(line[84..89]),
				getAlt(line[89..94])
			  );

		AltitudeMSL? transitionAltitude = getAlt(line[94..99]);
		SpeedRestriction speedRestriction =
			string.IsNullOrWhiteSpace(line[99..102])
			? SpeedRestriction.Unrestricted
			: new(null, uint.Parse(line[99..102]));

		CheckEmpty(line, 102, 117);
		char speedLimit = line[117]; // ??? Looks like either '-' or ' ', not really sure why.
		CheckEmpty(line, 118, 123);

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, airport, name, routeType, transitionIdentifier, initialFix, pathTerm,
			ep ?? fix, procVia, altitudeRestriction, speedRestriction, frn, cycle);
	}

	public static bool TryParse(string line, [NotNullWhen(true)] out STARLine? result)
	{
		try
		{
			if (Parse(line) is STARLine sl)
			{
				result = sl;
				return true;
			}
		}
		catch (FormatException) { }

		result = null;
		return false;
	}

	public enum STARRouteType
	{
		EnrouteTransition = '1',
		CommonRoute = '2',
		RunwayTransition = '3',
		EnrouteTransition_RNAV = '4',
		CommonRoute_RNAV = '5',
		RunwayTransition_RNAV = '6'
	}
}

public record ApproachLine(string Client,
	string Airport, string Name, ApproachLine.ApproachRouteType RouteType, string Transition, PathTermination FixInstruction,
	IProcedureEndpoint? Endpoint, IProcedureVia? Via, string? ReferencedNavaid, AltitudeRestriction AltitudeRestriction, SpeedRestriction SpeedRestriction,
	int FileRecordNumber, int Cycle) : ProcedureLine(Client, "PF", Airport, Name, FileRecordNumber, Cycle)
{
	public static new ApproachLine? Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];

		Check(line, 4, 6, "P ", "H ");
		string airport = line[6..10].Trim();
		string icaoRegion = line[10..12];

		Check(line, 12, 13, "F");
		string name = line[13..19].Trim();
		ApproachRouteType routeType = (ApproachRouteType)line[19];
		string transitionIdentifier = line[20..25].Trim();

		if (routeType != ApproachRouteType.Transition)
			CheckEmpty(line, 20, 25);

		CheckEmpty(line, 25, 26);
		int sequenceNum = int.Parse(line[26..29]);

		UnresolvedWaypoint? fix = line[29..34].Trim() == "" ? null : new(line[29..34].Trim());
		string fix_icaoRegion = line[34..36];
		string fixWPTerminalType = line[36..38];

		char continuationNum = line[38]; // Continuation number. Anything more than 1 is a problem.
		_ = line[39..43]; // ??? Here's that weird DESC CODE thing again. Still no clue how to read it.

		if (continuationNum > '1')
			return null;

		ApproachFixLabel fixLabel = (ApproachFixLabel)line[43];

		int? rnp = string.IsNullOrWhiteSpace(line[44..47]) ? null : int.Parse(line[44..47]);
		bool overfly = rnp is not null && rnp % 10 == 1;

		string? referencedNavaid = null;
		(PathTermination pathTerm, IProcedureEndpoint? ep, IProcedureVia? procVia) = GetPathSegment(line);
		if (procVia is Racetrack r)
			procVia = r with { Waypoint = fix };
		else if (pathTerm.HasFlag(PathTermination.Course) && !string.IsNullOrWhiteSpace(line[50..54]))
			referencedNavaid = line[50..54].Trim();

		static AltitudeMSL? getAlt(string data) =>
			string.IsNullOrWhiteSpace(data)
			? null
			: data[0..2] == "FL" ? new FlightLevel(int.Parse(data[2..])) : new AltitudeMSL(int.Parse(data));

		AltitudeRestriction altitudeRestriction =
			AltitudeRestriction.FromDescription(
				(AltitudeRestriction.AltitudeDescription)line[82],
				getAlt(line[84..89]),
				getAlt(line[89..94])
			);

		AltitudeMSL? transitionAltitude = getAlt(line[94..99]);
		SpeedRestriction speedRestriction =
			string.IsNullOrWhiteSpace(line[99..102])
			? SpeedRestriction.Unrestricted
			: new(null, uint.Parse(line[99..102]));

		decimal? verticalAngle = string.IsNullOrWhiteSpace(line[102..106]) ? null : decimal.Parse(line[102..106]) / 100;
		string centerFix = line[106..111];

		char msaMultiCode = line[111]; // If multiple MSAs are available, which applies?

		string centerFixIcaoRegion = line[112..114];
		string centerFixType = line[114..116];

		char gnssIndicator = line[116];
		char speedLimit = line[117]; // ??? Looks like either '-' or ' ', not really sure why.

		string routeQualifiers = line[118..120];

		CheckEmpty(line, 120, 123);

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, airport, name, routeType, transitionIdentifier, pathTerm, ep ?? fix, procVia, referencedNavaid,
			altitudeRestriction, speedRestriction, frn, cycle);
	}

	public static bool TryParse(string line, [NotNullWhen(true)] out ApproachLine? result)
	{
		try
		{
			if (Parse(line) is ApproachLine al)
			{
				result = al;
				return true;
			}
		}
		catch (FormatException) { }

		result = null;
		return false;
	}

	public enum ApproachRouteType
	{
		Transition = 'A',
		LocalizerBackCourse = 'B',
		VORDME = 'D',
		RNP = 'H',
		ILS = 'I',
		Localizer = 'L',
		NDB = 'N',
		GPS = 'P',
		NDBDME = 'Q',
		RNAV = 'R',
		VORDME_VOROnly = 'S',
		SDF = 'U',
		VOR = 'V',
		LDA = 'X'
	}

	public enum ApproachFixLabel
	{
		IAF = 'A',
		IF = 'B',
		Intercept = 'I',
		FAF = 'F',
		Mapt = 'M'
	}
}

[JsonConverter(typeof(IProcedureEndpointJsonConverter))]
public interface IProcedureEndpoint
{
	public bool IsConditionReached(PathTermination termination, (Coordinate position, Altitude altitude, dynamic? reference) context, decimal tolerance);

	public class IProcedureEndpointJsonConverter : JsonConverter<IProcedureEndpoint>
	{
		public override IProcedureEndpoint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.StartArray)
				return JsonSerializer.Deserialize<ICoordinate>(ref reader, options);
			else if (reader.TokenType == JsonTokenType.StartObject)
			{
				reader.Read();
				if (reader.TokenType != JsonTokenType.PropertyName)
					throw new JsonException();

				var prop = reader.GetString();
				reader.Read();

				IProcedureEndpoint retval =
					 prop switch {
						 "distance" => JsonSerializer.Deserialize<Distance>(ref reader, options)!,
						 "radial" => JsonSerializer.Deserialize<Radial>(ref reader, options)!,
						 _ => throw new JsonException()
					 };
				reader.Read();

				if (reader.TokenType != JsonTokenType.EndObject)
					throw new JsonException();

				return retval;
			}
			else
				throw new JsonException();
		}

		public override void Write(Utf8JsonWriter writer, IProcedureEndpoint value, JsonSerializerOptions options)
		{
			switch (value)
			{
				case ICoordinate c:
					JsonSerializer.Serialize(writer, c, options);
					break;

				case Distance d:
					writer.WriteStartObject();
					writer.WritePropertyName("distance");
					JsonSerializer.Serialize(writer, d, options);
					writer.WriteEndObject();
					break;

				case Radial r:
					writer.WriteStartObject();
					writer.WritePropertyName("radial");
					JsonSerializer.Serialize(writer, r, options);
					writer.WriteEndObject();
					break;

				default:
					throw new JsonException();
			}
		}
	}
}

[JsonConverter(typeof(IProcedureViaJsonConverter))]
public interface IProcedureVia
{
	public TrueCourse GetTrueCourse(Coordinate position, Course currentCourse, TimeSpan refreshRate, bool onGround);

	protected static TrueCourse TurnTowards(Course currentCourse, Course targetCourse, TimeSpan refreshRate, bool onGround, bool? forceLeftTurn = null)
	{
		decimal angleRemaining = currentCourse.Angle(targetCourse);
		decimal standardRate = 3m * (decimal)refreshRate.TotalSeconds;
		if (onGround || Math.Abs(angleRemaining) < standardRate)
			return targetCourse.ToTrue();

		bool turnLeft = forceLeftTurn ?? angleRemaining < 0;

		if (turnLeft)
			return (currentCourse.ToTrue() - standardRate).ToTrue();
		else
			return (currentCourse.ToTrue() + standardRate).ToTrue();
	}

	public class IProcedureViaJsonConverter : JsonConverter<IProcedureVia>
	{
		public override IProcedureVia? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
				throw new JsonException();

			reader.Read();
			if (reader.GetString() != "Type")
				throw new JsonException();

			reader.Read();
			string type = reader.GetString() ?? throw new JsonException();
			reader.Read();
			if (reader.GetString() != "Data")
				throw new JsonException();

			reader.Read();
			IProcedureVia? retval = type switch {
				"Arc" => JsonSerializer.Deserialize<Arc>(ref reader, options),
				"Course" => JsonSerializer.Deserialize<Course>(ref reader, options),
				"Racetrack" => JsonSerializer.Deserialize<Racetrack>(ref reader, options),
				_ => throw new JsonException()
			};
			reader.Read();

			if (reader.TokenType != JsonTokenType.EndObject)
				throw new JsonException();

			return retval;
		}

		public override void Write(Utf8JsonWriter writer, IProcedureVia value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();

			switch (value)
			{
				case Arc a:
					writer.WriteString("Type", "Arc");
					writer.WritePropertyName("Data");
					JsonSerializer.Serialize(writer, a, options);
					break;

				case Course c:
					writer.WriteString("Type", "Course");
					writer.WritePropertyName("Data");
					JsonSerializer.Serialize(writer, c, options);
					break;

				case Racetrack r:
					writer.WriteString("Type", "Racetrack");
					writer.WritePropertyName("Data");
					JsonSerializer.Serialize(writer, r, options);
					break;
			}

			writer.WriteEndObject();
		}
	}
}