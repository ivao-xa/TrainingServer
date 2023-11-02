using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace TrainingServer.Extensibility;

[JsonConverter(typeof(CoordinateJsonConverter))]
public record struct Coordinate(float Latitude, float Longitude)
{
	public readonly Coordinate GetCoordinate() => this;

	public Coordinate(double latitude, double longitude) : this((float)latitude, (float)longitude) { }

	public Coordinate(string coordData) : this(0, 0)
	{
		static float dmsToDec(string dms)
		{
			int degrees = int.Parse(dms[0..3]);
			int minutes = dms.Length > 3 ? int.Parse(dms[3..5]) : 0;
			float seconds = dms.Length > 5 ? float.Parse(dms[5..7] + '.' + dms[7..]) : 0f;

			return degrees + (minutes + (seconds / 60f)) / 60f;
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

	public readonly PointF ToPoint() => new(Longitude, Latitude);

	[JsonIgnore]
	public readonly string DMS
	{
		get
		{
			static string decToDms(float dec)
			{
				dec *= dec < 0 ? -1 : 1;

				int degrees = (int)dec;
				int minutes = (int)((dec - degrees) * 60);
				float seconds = ((dec - degrees) * 60 - minutes) * 60;

				return $"{degrees:000}{minutes:00}{(int)seconds:00}";
			}

			return decToDms(Latitude)[1..] + (Latitude < 0 ? 'S' : 'N') + decToDms(Longitude) + (Longitude < 0 ? 'W' : 'E');
		}
	}

	/// <summary>
	/// Returns a <see cref="Coordinate"/> which is a given <paramref name="distance"/> along a given <paramref name="bearing"/> from <see langword="this"/>.
	/// </summary>
	/// <param name="bearing">The true <see cref="Bearing"/> from <see langword="this"/>.</param>
	/// <param name="distance">The distance (in nautical miles) from <see langword="this"/>.</param>
	[DebuggerStepThrough]
	public readonly Coordinate FixRadialDistance(float bearing, float distance)
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
		double alpha1 = bearing * DEG_TO_RAD;

		double U1 = Math.Atan((1 - f) * Math.Tan(phi1));

		double sigma1 = Math.Atan2(Math.Tan(U1), cos(alpha1));
		double alpha = Math.Asin(cos(U1) * sin(alpha1));

		double uSquared = square(cos(alpha)) * ((square(a) - square(b)) / square(b));
		double A = 1 + (uSquared / 16384) * (4096 + uSquared * (-768 + uSquared * (320 - 175 * uSquared)));
		double B = (uSquared / 1024) * (256 + uSquared * (-128 + uSquared * (74 - 47 * uSquared)));

		double sigma = distance / b / A,
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
			sigma = distance / b / A + deltaSigma;
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

		return new((float)phi2, (float)L2);
	}

	[DebuggerStepThrough]
	public readonly (float? bearing, float distance) GetBearingDistance(Coordinate other)
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
			return (null, 0f);
		else if (double.IsNaN(alpha_1))
			return (null, (float)s);

		return ((float)(alpha_1 * RAD_TO_DEG), (float)s);
	}

	[DebuggerStepThrough]
	public readonly float DistanceTo(Coordinate other)
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

		return (float)(R * c);
	}

	public static Coordinate operator +(Coordinate left, Coordinate right) =>
		new(Math.Clamp(left.Latitude + right.Latitude, -90f, 90), Math.Clamp(left.Longitude + right.Longitude, -180f, 180));
	public static Coordinate operator -(Coordinate left, Coordinate right) =>
		new(Math.Clamp(left.Latitude - right.Latitude, -90f, 90), Math.Clamp(left.Longitude - right.Longitude, -180f, 180));

	public static Coordinate operator *(Coordinate left, float right) =>
		new(Math.Clamp(left.Latitude * right, -90f, 90), Math.Clamp(left.Longitude * right, -180f, 180));
	public static Coordinate operator /(Coordinate left, float right) =>
		new(Math.Clamp(left.Latitude / right, -90f, 90), Math.Clamp(left.Longitude / right, -180f, 180));

	public override readonly string ToString() => $"({Math.Round(Math.Abs(Latitude), 2)}, {Math.Round(Math.Abs(Longitude), 2)})";

	public class CoordinateJsonConverter : JsonConverter<Coordinate>
	{
		public override Coordinate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartArray)
				throw new JsonException();

			reader.Read();
			float lat = reader.GetSingle();
			reader.Read();
			float lon = reader.GetSingle();
			reader.Read();

			if (reader.TokenType != JsonTokenType.EndArray)
				throw new JsonException();

			return new(lat, lon);
		}

		public override void Write(Utf8JsonWriter writer, Coordinate value, JsonSerializerOptions options)
		{
			writer.WriteStartArray();
			writer.WriteNumberValue(MathF.Round(value.Latitude, 6));
			writer.WriteNumberValue(MathF.Round(value.Longitude, 6));
			writer.WriteEndArray();
		}
	}
}