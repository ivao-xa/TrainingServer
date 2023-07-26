using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using static CIFPReader.ProcedureLine;

namespace CIFPReader;

[JsonConverter(typeof(ICoordinateJsonConverter))]
public interface ICoordinate : IProcedureEndpoint
{
	public decimal Latitude { get; }
	public decimal Longitude { get; }

	public Coordinate GetCoordinate();

	public class ICoordinateJsonConverter : JsonConverter<ICoordinate>
	{
		public override ICoordinate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartArray)
				throw new JsonException();

			Utf8JsonReader explorer = reader;

			explorer.Read();
			if (explorer.TokenType is JsonTokenType.Number)
				return JsonSerializer.Deserialize<Coordinate>(ref reader, options);
			else if (explorer.TokenType is JsonTokenType.String)
				return JsonSerializer.Deserialize<NamedCoordinate>(ref reader, options);
			else
				throw new JsonException();
		}

		public override void Write(Utf8JsonWriter writer, ICoordinate value, JsonSerializerOptions options)
		{
			switch (value)
			{
				case Coordinate c:
					JsonSerializer.Serialize(writer, c, options);
					break;

				case NamedCoordinate nc:
					JsonSerializer.Serialize(writer, nc, options);
					break;

				default:
					throw new JsonException();
			}
		}
	}
}

[JsonConverter(typeof(NamedCoordinateJsonConverter))]
public record struct NamedCoordinate(string Name, Coordinate Position) : IProcedureEndpoint, ICoordinate
{
	public readonly decimal Latitude => Position.Latitude;
	public readonly decimal Longitude => Position.Longitude;

	public readonly Coordinate GetCoordinate() => Position;

	public readonly bool IsConditionReached(PathTermination termination, (Coordinate position, Altitude altitude, dynamic? reference) context, decimal tolerance) => 
		Position.IsConditionReached(termination, context, tolerance);
	
	public class NamedCoordinateJsonConverter : JsonConverter<NamedCoordinate>
	{
		public override NamedCoordinate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartArray)
				throw new JsonException();

			reader.Read();
			string name = reader.GetString() ?? throw new JsonException();
			reader.Read();
			decimal lat = reader.GetDecimal();
			reader.Read();
			decimal lon = reader.GetDecimal();
			reader.Read();

			if (reader.TokenType != JsonTokenType.EndArray)
				throw new JsonException();

			return new(name, new(lat, lon));
		}

		public override void Write(Utf8JsonWriter writer, NamedCoordinate value, JsonSerializerOptions options)
		{
			writer.WriteStartArray();
			writer.WriteStringValue(value.Name);
			writer.WriteNumberValue(decimal.Round(value.Position.Latitude, 6));
			writer.WriteNumberValue(decimal.Round(value.Position.Longitude, 6));
			writer.WriteEndArray();
		}
	}
}

[JsonConverter(typeof(CoordinateJsonConverter))]
public record struct Coordinate(decimal Latitude, decimal Longitude) : IProcedureEndpoint, ICoordinate
{
	public readonly Coordinate GetCoordinate() => this;

	public Coordinate(string coordData) : this(0, 0)
	{
		static decimal dmsToDec(string dms)
		{
			int degrees = int.Parse(dms[0..3]);
			int minutes = dms.Length > 3 ? int.Parse(dms[3..5]) : 0;
			decimal seconds = dms.Length > 5 ? decimal.Parse(dms[5..7] + '.' + dms[7..]) : 0m;

			return degrees + (minutes + (seconds / 60m)) / 60m;
		}

		coordData = coordData.Trim();
		if (coordData.Length < 7 || !"NS".Contains(coordData[0]) || !(coordData.Contains('E') || coordData.Contains('W')))
			throw new ArgumentException("Cannot parse coordinate " + coordData);

		int splitpoint = Math.Max(coordData.IndexOf('E'), coordData.IndexOf('W'));
		if (Math.Abs(coordData.Length / 2 - splitpoint) > 1)
			throw new ArgumentException("Misaligned coordinate " + coordData);

		if (splitpoint == 3)
		{
			Latitude = int.Parse(coordData[1..splitpoint]);
			Longitude = int.Parse(coordData[(splitpoint + 1)..]);
		}
		else
		{
			Latitude = dmsToDec('0' + coordData[1..splitpoint]);
			Longitude = dmsToDec(coordData[(splitpoint + 1)..]);
		}

		if (coordData[0] == 'S')
			Latitude *= -1;
		if (coordData[splitpoint] == 'W')
			Longitude *= -1;
	}

	[JsonIgnore]
	public readonly string DMS
	{
		get
		{
			static string decToDms(decimal dec)
			{
				dec *= dec < 0 ? -1 : 1;

				int degrees = (int)dec;
				dec %= 1;
				dec *= 60;
				int minutes = (int)dec;
				dec %= 1;
				dec *= 60;

				return $"{degrees:000}{minutes:00}{(int)dec:00}";
			}

			return decToDms(Latitude)[1..] + (Latitude < 0 ? 'S' : 'N') + decToDms(Longitude) + (Longitude < 0 ? 'W' : 'E');
		}
	}

	[JsonIgnore]
	public readonly string DMSLeadingDirections
	{
		get
		{
			static string decToDms(decimal dec)
			{
				dec *= dec < 0 ? -1 : 1;

				int degrees = (int)dec;
				dec %= 1;
				dec *= 60;
				int minutes = (int)dec;
				dec %= 1;
				dec *= 60;

				return $"{degrees:000}{minutes:00}{(int)(dec * 100):0000}";
			}

			return (Latitude < 0 ? 'S' : 'N') + decToDms(Latitude)[1..] + (Longitude < 0 ? 'W' : 'E') + decToDms(Longitude);
		}
	}

	public readonly NamedCoordinate Name(string name) => new(name, this);

	/// <summary>
	/// Returns a <see cref="Coordinate"/> which is a given <paramref name="distance"/> along a given <paramref name="bearing"/> from <see langword="this"/>.
	/// </summary>
	/// <param name="bearing">The true <see cref="Bearing"/> from <see langword="this"/>.</param>
	/// <param name="distance">The distance (in nautical miles) from <see langword="this"/>.</param>
	[DebuggerStepThrough]
	public readonly Coordinate FixRadialDistance(Course bearing, decimal distance)
	{
		// Vincenty's formulae
		const double a = 3443.918;
		const double b = 3432.3716599595;
		const double f = 1 / 298.257223563;
		const double DEG_TO_RAD = Math.Tau / 360;
		const double RAD_TO_DEG = 360 / Math.Tau;
		static double square(double x) => x * x;
		static double cos(double x) => Math.Cos(x);
		static double sin(double x) => Math.Sin(x);

		double phi1 = (double)Latitude * DEG_TO_RAD;
		double L1 = (double)Longitude * DEG_TO_RAD;
		double alpha1 = (double)bearing.ToTrue().Radians;
		double s = (double)distance;

		double U1 = Math.Atan((1 - f) * Math.Tan(phi1));

		double sigma1 = Math.Atan2(Math.Tan(U1), cos(alpha1));
		double alpha = Math.Asin(cos(U1) * sin(alpha1));

		double uSquared = square(cos(alpha)) * ((square(a) - square(b)) / square(b));
		double A = 1 + (uSquared / 16384) * (4096 + uSquared * (-768 + uSquared * (320 - 175 * uSquared)));
		double B = (uSquared / 1024) * (256 + uSquared * (-128 + uSquared * (74 - 47 * uSquared)));

		double sigma = s / b / A,
			   oldSigma = sigma - 100;

		double twoSigmaM = double.NaN;

		while (Math.Abs(sigma - oldSigma) > 1.0E-9)
		{
			twoSigmaM = 2 * sigma1 + sigma;

			double cos_2_sigmaM = cos(twoSigmaM);

			double deltaSigma = B * sin(sigma) * (
					cos_2_sigmaM + 0.25 * B * (
						cos(sigma) * (
							-1 + 2 * square(cos_2_sigmaM)
						) - (B / 6) * cos_2_sigmaM * (
							-3 + 4 * square(sin(sigma))
						) * (
							-3 + 4 * square(cos_2_sigmaM)
						)
					)
				);
			oldSigma = sigma;
			sigma = s / b / A + deltaSigma;
		}

		(double sin_sigma, double cos_sigma) = Math.SinCos(sigma);
		(double sin_alpha, double cos_alpha) = Math.SinCos(alpha);
		(double sin_U1, double cos_U1) = Math.SinCos(U1);

		double phi2 = Math.Atan2(sin_U1 * cos_sigma + cos_U1 * sin_sigma * cos(alpha1),
								 (1 - f) * Math.Sqrt(square(sin_alpha) + square(sin_U1 * sin_sigma - cos_U1 * cos_sigma * cos(alpha1))));
		double lambda = Math.Atan2(sin_sigma * sin(alpha1),
								   cos_U1 * cos_sigma - sin_U1 * sin_sigma * cos(alpha1));

		double C = (f / 16) * square(cos_alpha) * (4 + f * (4 - 3 * square(cos_alpha)));
		double L = lambda - (1 - C) * f * sin_alpha * (sigma + C * sin_sigma * (cos(2 * twoSigmaM) + C * cos_sigma * (-1 + 2 * square(cos(2 * twoSigmaM)))));

		double L2 = L + L1;

		phi2 *= RAD_TO_DEG;
		L2 *= RAD_TO_DEG;

		return new((decimal)phi2, (decimal)L2);
	}

	[DebuggerStepThrough]
	public readonly (TrueCourse? bearing, decimal distance) GetBearingDistance(Coordinate other)
	{
		if (this == other)
			return (null, 0);

		// Inverse Vincenty
		const double a = 3443.918;
		const double b = 3432.3716599595;
		const double f = 1 / 298.257223563;
		const double DEG_TO_RAD = Math.Tau / 360;
		const double RAD_TO_DEG = 360 / Math.Tau;
		static double square(double x) => x * x;
		static double cos(double x) => Math.Cos(x);
		static double sin(double x) => Math.Sin(x);

		double phi1 = (double)Latitude * DEG_TO_RAD,
			   L1 = (double)Longitude * DEG_TO_RAD,
			   phi2 = (double)other.Latitude * DEG_TO_RAD,
			   L2 = (double)other.Longitude * DEG_TO_RAD;

		double U1 = Math.Atan((1 - f) * Math.Tan(phi1)),
			   U2 = Math.Atan((1 - f) * Math.Tan(phi2)),
			   L = L2 - L1;

		double lambda = L, oldLambda;

		(double sin_U1, double cos_U1) = Math.SinCos(U1);
		(double sin_U2, double cos_U2) = Math.SinCos(U2);

		double cos_2_alpha = 0, sin_sigma = 0, cos_sigma = 0, sigma = 0, cos_2_sigmaM = 0;

		for (int iterCntr = 0; iterCntr < 100; ++iterCntr)
		{
			sin_sigma = Math.Sqrt(
					square(
						cos_U2 * sin(lambda)
					) + square(
						(cos_U1 * sin_U2) - (sin_U1 * cos_U2 * cos(lambda))
					)
				);

			cos_sigma = sin_U1 * sin_U2 + cos_U1 * cos_U2 * cos(lambda);

			sigma = Math.Atan2(sin_sigma, cos_sigma);

			double sin_alpha = (cos_U1 * cos_U2 * sin(lambda)) / sin_sigma;

			cos_2_alpha = 1 - square(sin_alpha);

			cos_2_sigmaM = cos_sigma - (2 * sin_U1 * sin_U2 / cos_2_alpha);

			double C = f / 16 * cos_2_alpha * (4 + f * (4 - 3 * cos_2_alpha));

			oldLambda = lambda;
			lambda = L + (1 - C) * f * sin_alpha * (sigma + C * sin_sigma * (cos_2_sigmaM) + C * cos_sigma * (-1 + 2 * square(cos_2_sigmaM)));

			if (Math.Abs(lambda - oldLambda) > 1.0E-9)
				break;
		}

		double u2 = cos_2_alpha * ((square(a) - square(b)) / square(b));

		double A = 1 + u2 / 16384 * (4096 + u2 * (-768 + u2 * (320 - 175 * u2))),
			   B = u2 / 1024 * (256 + u2 * (-128 + u2 * (74 - 47 * u2)));

		double delta_sigma = B * sin_sigma * (cos_2_sigmaM + 1 / 4 * B * (cos_sigma * (-1 + 2 * square(cos_2_sigmaM)) - B / 6 * cos_2_sigmaM * (-3 + 4 * square(sin_sigma)) * (-3 + 4 * square(cos_2_sigmaM))));

		double s = b * A * (sigma - delta_sigma);
		double alpha_1 = Math.Atan2(
				cos_U2 * sin(lambda),
				cos_U1 * sin_U2 - sin_U1 * cos_U2 * cos(lambda)
			);

		if (double.IsNaN(s))
			return (null, 0m);
		else if (double.IsNaN(alpha_1))
			return (null, (decimal)s);

		return (new((decimal)(alpha_1 * RAD_TO_DEG)), (decimal)s);
	}

	[DebuggerStepThrough]
	public readonly decimal DistanceTo(Coordinate other)
	{
		const double R = 3440.07;
		const double DEG_TO_RAD = Math.Tau / 360;

		double dlat = (double)(other.Latitude - Latitude) * DEG_TO_RAD,
			   dlon = (double)(other.Longitude - Longitude) * DEG_TO_RAD;

		double lat1 = (double)Latitude * DEG_TO_RAD,
			   lat2 = (double)other.Latitude * DEG_TO_RAD;

		double sinDLatOver2 = Math.Sin(dlat / 2),
			   sinDLonOver2 = Math.Sin(dlon / 2);

		double a = sinDLatOver2 * sinDLatOver2 +
				   sinDLonOver2 * sinDLonOver2 * Math.Cos(lat1) * Math.Cos(lat2);

		double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

		return (decimal)(R * c);
	}

	/// <summary>
	/// Returns a <see cref="Coordinate"/> which intersects a given <paramref name="radial"/> along a given <paramref name="bearing"/> from <see langword="this"/>.
	/// </summary>
	/// <param name="bearing">The true <see cref="Bearing"/> from <see langword="this"/>.</param>
	/// <param name="radial">The <see cref="Radial"/> to intersect.</param>
	/// <param name="tolerance">The number of degrees within which the returned <see cref="Coordinate"/> must lie from the true point of intersection.</param>
	[DebuggerStepThrough]
	public readonly Coordinate RadialIntersect(Course bearing, Radial radial, decimal tolerance = 0.5m)
	{
		if (radial.Station.Position.GetBearingDistance(this).bearing is null)
			return this;

		Coordinate exploratory = new(Latitude, Longitude);

		void stepTowards()
		{
			var data = radial.Station.Position.GetBearingDistance(exploratory);
			if (data.bearing is null)
				throw new Exception();

			var stepSize = data.distance * Math.Abs(data.bearing.Angle(radial.Bearing));

			var guess = exploratory.FixRadialDistance(bearing, stepSize);
			if (radial.Station.Position.DistanceTo(guess) < data.distance)
				exploratory = guess;
			else
				exploratory = exploratory.FixRadialDistance(bearing, -stepSize);
		}

		while (radial.Station.Position.GetBearingDistance(exploratory).bearing is Course newBearing && Math.Abs(radial.Bearing.Angle(newBearing)) > tolerance)
			stepTowards();

		return exploratory;
	}

	public static Coordinate operator +(Coordinate left, Coordinate right) =>
		new(left.Latitude + right.Latitude, left.Longitude + right.Longitude);

	public static Coordinate operator -(Coordinate left, Coordinate right) =>
		new(left.Latitude - right.Latitude, left.Longitude - right.Longitude);

	public readonly bool IsConditionReached(PathTermination termination, (Coordinate position, Altitude altitude, dynamic? reference) context, decimal tolerance)
	{
		(Coordinate position, Altitude _, dynamic? reference) = context;
		// Termination
		//UntilCrossing = 0b_0000001,
		//UntilAltitude = 0b_0000010,
		//UntilDistance = 0b_0000100,
		//UntilIntercept = 0b_0001000,
		//UntilRadial = 0b_0010000,
		//ForDistance = 0b_0100000,
		//UntilTerminated = 0b_1000000,

		if (termination.HasFlag(PathTermination.UntilCrossing))
		{
			if (termination.HasFlag(PathTermination.Course) && reference is Coordinate cr)
			{
				TrueCourse? item1 = GetBearingDistance(cr).bearing;
				TrueCourse? item2 = GetBearingDistance(position).bearing;
				if (item1 is null && item2 is null)
					return true;
				else if (item1 is not null && item2 is not null)
					return Math.Abs(item2.Angle(item1)) - (90 - tolerance) > 0;
				else if (item1 is null)
					throw new ArgumentException("Reference point should not be the same as the endpoint.");
				else
					return true; // Directly on top of the endpoint.
			}
			else if (reference is not null && ((Type)reference.GetType()).GetProperty("Overfly") is not null)
				return DistanceTo(position) <= 0.005m;
			else
				return DistanceTo(position) <= tolerance;
		}
		else if (termination.HasFlag(PathTermination.ForDistance) && reference is Coordinate)
			// Hack it in for now
			return IsConditionReached(termination | PathTermination.UntilCrossing, context, tolerance);
		else
			throw new NotImplementedException();
	}

	public override string ToString() => $"({Latitude:00.000}, {Longitude:000.000})";

	public class CoordinateJsonConverter : JsonConverter<Coordinate>
	{
		public override Coordinate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartArray)
				throw new JsonException();

			reader.Read();
			decimal lat = reader.GetDecimal();
			reader.Read();
			decimal lon = reader.GetDecimal();
			reader.Read();

			if (reader.TokenType != JsonTokenType.EndArray)
				throw new JsonException();

			return new(lat, lon);
		}

		public override void Write(Utf8JsonWriter writer, Coordinate value, JsonSerializerOptions options)
		{
			writer.WriteStartArray();
			writer.WriteNumberValue(decimal.Round(value.Latitude, 6));
			writer.WriteNumberValue(decimal.Round(value.Longitude, 6));
			writer.WriteEndArray();
		}
	}
}