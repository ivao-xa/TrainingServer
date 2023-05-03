using System.Diagnostics.CodeAnalysis;

using static CIFPReader.ControlledAirspace;

namespace CIFPReader;

#pragma warning disable IDE0059

internal static class AirspaceLine
{
	public static RecordLine? Parse(string line) =>
		line[5] switch
		{
			'S' => GridMORA.Parse(line),
			'C' => ControlledAirspace.Parse(line),
			'R' => RestrictiveAirspace.Parse(line),

			_ => null
		};
}

public class Airspace
{
	private readonly List<SegmentRegion> SegmentRegions = new();


	public Airspace(params ControlledAirspace[] segments)
	{
		List<ControlledAirspace> segblob = new();
		foreach (ControlledAirspace seg in segments)
		{
			segblob.Add(seg);

			if (seg.Boundary.BoundaryVia.HasFlag(BoundaryViaType.ReturnToOrigin))
			{
				SegmentRegions.Add(SegmentRegion.FromSegments(segblob));
				segblob.Clear();
			}
		}

		if (segblob.Any())
			throw new ArgumentException("Last segment must return to origin.");
	}

	public bool Contains(Coordinate point, AltitudeMSL altitude) =>
		SegmentRegions.Any(sr => sr.Contains(point, altitude));

	protected abstract class SegmentRegion
	{
		protected ControlledAirspace[] Segments;

		public SegmentRegion(IEnumerable<ControlledAirspace> segments) =>
			Segments = segments.ToArray();

		public static SegmentRegion FromSegments(IEnumerable<ControlledAirspace> segments)
		{
			if (segments.Count() == 1 && segments.Single().Boundary is BoundaryCircle)
				return new CircularSegmentRegion(segments.Single());
			else if (segments.Any(ca => ca.Boundary is BoundaryArc))
				return new ArcSegmentRegion(segments);
			else if (segments.All(ca => ca.Boundary is BoundaryLine))
				return new LineSegmentRegion(segments);
			else
				throw new NotImplementedException();
		}

		public abstract bool Contains(Coordinate point, AltitudeMSL altitude);

		protected bool CheckAltitude(AltitudeMSL altitude)
		{
			(Altitude? Lower, Altitude? Upper) = Segments.First().VerticalBounds;
			if (Lower is null && Upper is null)
				throw new Exception("First segment of airspace must have altitude restrictions.");
			else if (Lower is AltitudeMSL lalt && altitude.Feet < lalt.Feet)
				return false;
			else if (Upper is AltitudeMSL ualt && altitude.Feet > ualt.Feet)
				return false;

			return true;
		}
	}

	protected class CircularSegmentRegion : SegmentRegion
	{
		protected BoundaryCircle Boundary => (BoundaryCircle)Segments.Single().Boundary;

		public CircularSegmentRegion(ControlledAirspace segment) : base(new[] { segment }) { }

		public override bool Contains(Coordinate point, AltitudeMSL altitude) =>
			Boundary.Centerpoint.GetCoordinate().DistanceTo(point) <= Boundary.Radius && CheckAltitude(altitude);
	}

	protected class ArcSegmentRegion : SegmentRegion
	{
		private readonly List<(BoundaryArc Arc, TrueCourse EndPoint)> _arcs = new();
		private readonly List<(Coordinate From, Coordinate To)> _lines = new();

		public ArcSegmentRegion(IEnumerable<ControlledAirspace> segments) : base(segments)
		{
			BoundarySegment[] boundaries = segments.Select(bs => bs.Boundary).ToArray();

			for (int cntr = 0; cntr < boundaries.Length; ++cntr)
			{
				if (boundaries[cntr] is not BoundaryLine line)
					continue;

				BoundarySegment next = boundaries[(cntr + 1) % boundaries.Length];

				_lines.Add((line.Vertex, next.Vertex));
			}

			for (int cntr = 0; cntr < boundaries.Length; ++cntr)
			{
				if (boundaries[cntr] is not BoundaryArc arc)
					continue;

				BoundarySegment next = boundaries[(cntr + 1) % boundaries.Length];

				(TrueCourse? tB, decimal tD) = arc.ArcOrigin.GetCoordinate().GetBearingDistance(next.Vertex);
				if (Math.Abs(tD - arc.ArcDistance) > 0.25m)
					throw new ArgumentException("Endpoint is more than .25nmi off of arc.");

				tB ??= new(360);

				_arcs.Add((arc, tB));
			}
		}

		public override bool Contains(Coordinate point, AltitudeMSL altitude)
		{
			static bool CrossesArc((BoundaryArc Arc, TrueCourse EndPoint) arc, Coordinate point, Coordinate target)
			{
				bool SubCheck(TrueCourse theta)
				{
					if (arc.Arc.BoundaryVia.HasFlag(BoundaryViaType.ClockwiseArc))
					{
						if (arc.Arc.ArcBearing < arc.EndPoint)
							return theta > arc.Arc.ArcBearing && theta < arc.EndPoint;
						else
							return theta > arc.Arc.ArcBearing || theta < arc.EndPoint;
					}
					else
					{
						if (arc.Arc.ArcBearing > arc.EndPoint)
							return theta < arc.Arc.ArcBearing && theta > arc.EndPoint;
						else
							return theta < arc.Arc.ArcBearing || theta > arc.EndPoint;
					}
				}

				static decimal square(decimal val) => val * val;
				static decimal sqrt(decimal val) => (decimal)Math.Sqrt((double)val);

				static decimal EuclideanDistance(Coordinate from, Coordinate to)
				{
					(decimal dy, decimal dx) = to - from;
					return sqrt(square(dx) + square(dy));
				}

				Coordinate refPoint = point - arc.Arc.ArcOrigin.GetCoordinate();

				// Turns out dividing by 60 ain't good enough
				decimal r = EuclideanDistance(arc.Arc.ArcOrigin.GetCoordinate().FixRadialDistance(new TrueCourse(90), arc.Arc.ArcDistance), arc.Arc.ArcOrigin.GetCoordinate());
				r += EuclideanDistance(refPoint.FixRadialDistance(new TrueCourse(90), arc.Arc.ArcDistance), refPoint);
				r /= 2;

				var (dy, dx) = target - point;
				decimal m = dy / dx,
						b = refPoint.Latitude - refPoint.Longitude * m;

				// x^2 + y^2 = r^2 (circle) and y = mx + b (line)
				// d = (m^2 + 1)r^2 - b^2

				decimal d = (square(m) + 1) * square(r) - square(b);

				if (d < 0)
					return false;

				decimal x1 = -((sqrt(d) + b * m) / (square(m) + 1)),
						x2 = (sqrt(d) - b * m) / (square(m) + 1);

				Coordinate[] intersections = new[]
				{
					new Coordinate(x1 * m + b, x1) + arc.Arc.ArcOrigin.GetCoordinate(),
					new Coordinate(x2 * m + b, x2) + arc.Arc.ArcOrigin.GetCoordinate()
				};

				(decimal minlat, decimal minlon, decimal maxlat, decimal maxlon) =
					(Math.Min(point.Latitude, target.Latitude), Math.Min(point.Longitude, target.Longitude),
					 Math.Max(point.Latitude, target.Latitude), Math.Max(point.Longitude, target.Longitude));

				// Only intersections on the segment matter
				intersections = intersections.Where(intersection =>
					   (intersection.Latitude >= minlat && intersection.Latitude <= maxlat)
					&& (intersection.Longitude >= minlon && intersection.Longitude <= maxlon)).ToArray();

				if (!intersections.Any())
					return false;

				TrueCourse[] isctBrngs = intersections.Select(intersection =>
				{
					(decimal offsety, decimal offsetx) = intersection - arc.Arc.ArcOrigin.GetCoordinate();

					decimal classicAngle = (decimal)(Math.Atan2((double)offsety, (double)offsetx) * (360 / Math.Tau));
					decimal inverseAngle = 360 - classicAngle;

					return new TrueCourse(inverseAngle + 90);
				}).ToArray();

				return isctBrngs.Count(SubCheck) % 2 == 1;
			}

			static bool CrossesLine((Coordinate From, Coordinate To) line, Coordinate point, Coordinate target)
			{
				// https://www.geeksforgeeks.org/check-if-two-given-line-segments-intersect/
				static bool Orientation(Coordinate p, Coordinate q, Coordinate r) =>
					((q.Latitude - p.Latitude) * (r.Longitude - q.Longitude) -
					 (q.Longitude - p.Longitude) * (r.Latitude - q.Latitude)) > 0;

				bool o1 = Orientation(line.From, line.To, point),
					 o2 = Orientation(line.From, line.To, target),
					 o3 = Orientation(point, target, line.From),
					 o4 = Orientation(point, target, line.To);

				return (o1 != o2) && (o3 != o4);
			}

			if (!CheckAltitude(altitude))
				return false;

			Coordinate[] verticies = Segments.Select(s => s.Boundary.Vertex).ToArray();
			(decimal minlat, decimal minlon, decimal maxrad) = (verticies.Select(v => v.Latitude).Min(), verticies.Select(v => v.Longitude).Min(), (_arcs.Select(d => d.Arc.ArcDistance).Max() + .1m) / 60m);
			Coordinate referencePoint = new(minlat - maxrad, minlon - maxrad);

			int linesCrossed = _lines.Count(l => CrossesLine(l, point, referencePoint)),
				arcsCrossed = _arcs.Count(a => CrossesArc(a, point, referencePoint));

			return (linesCrossed + arcsCrossed) % 2 == 1;
		}
	}

	protected class LineSegmentRegion : SegmentRegion
	{
		protected readonly Coordinate[] _verticies;

		public LineSegmentRegion(IEnumerable<ControlledAirspace> segments) : base(segments) =>
			_verticies = segments.Select(seg => ((BoundaryLine)seg.Boundary).Vertex).ToArray();

		public override bool Contains(Coordinate point, AltitudeMSL altitude)
		{
			if (!CheckAltitude(altitude))
				return false;

			(decimal x, decimal y) = point;
			int n = _verticies.Length;

			bool inside = true;
			int lim = n;

			for (int i = 0, j = n - 1; i < lim; j = i++)
			{
				Coordinate a = _verticies[i],
						   b = _verticies[j];

				(decimal xi, decimal yi) = a;
				(decimal xj, decimal yj) = b;

				if (yj < yi)
				{
					if (yj < y && y < yi)
					{
						decimal s = Orient3(a, b, point);
						if (s == 0)
							return true;
						else
							inside ^= 0 < s;
					}
					else if (y == yi)
					{
						(_, decimal yk) = _verticies[(i + 1) % n];

						if (yi < yk)
						{
							decimal s = Orient3(a, b, point);

							if (s == 0)
								return true;
							else
								inside ^= 0 < s;
						}
					}
				}
				else if (yi < yj)
				{
					if (yi < y && y < yj)
					{
						decimal s = Orient3(a, b, point);

						if (s == 0)
							return true;
						else
							inside ^= s < 0;
					}
					else if (y == yi)
					{
						(_, decimal yk) = _verticies[(i + 1) % n];

						if (yk < yi)
						{
							decimal s = Orient3(a, b, point);

							if (s == 0)
								return true;
							else
								inside ^= s < 0;
						}
					}
				}
				else if (y == yi)
				{
					(decimal x0, decimal x1) = (Math.Min(xi, xj), Math.Max(xi, xj));

					if (i == 0)
					{
						while (j > 0)
						{
							int k = (j + n - 1) % n;
							(decimal xp, decimal yp) = _verticies[k];

							if (yp != y)
								break;

							(x0, x1) = (Math.Min(x0, xp), Math.Max(x1, xp));
							j = k;
						}

						if (j == 0)
							return x0 <= x && x <= x1;

						lim = j + 1;
					}

					(_, decimal y0) = _verticies[(j + n - 1) % n];

					for (; i + 1 < lim; ++i)
					{
						(decimal xp, decimal yp) = _verticies[i + 1];
						if (yp != y)
							break;

						(x0, x1) = (Math.Min(x0, xp), Math.Max(x1, xp));
					}

					if (x0 <= x && x <= x1)
						return true;

					(_, decimal y1) = _verticies[(i + 1) % n];
					if (x < x0 && (y0 < y != y1 < y))
						inside = !inside;
				}
			}
			return !inside;
		}

		/// <summary>
		/// Calculates the orientation of three points.
		/// </summary>
		/// <remarks>https://github.com/mikolalysenko/robust-orientation/blob/master/orientation.js</remarks>
		/// <returns>&lt; 0 if positive, &gt; 0 if negative, &eq; 0 if coplanar.</returns>
		private static decimal Orient3(Coordinate a, Coordinate b, Coordinate c)
		{
			const decimal EPSILON = 1.1102230246251565e-16m;
			const decimal ERRBOUND3 = (3.0m + 16.0m * EPSILON) * EPSILON;

			var ((ax, ay), (bx, by), (cx, cy)) = (a, b, c);

			decimal l = (ay - cy) * (bx - cx),
					r = (ax - cx) * (by - cy);
			decimal det = l - r;
			decimal s;

			if (l > 0)
			{
				if (r <= 0)
					return det;
				else
					s = l + r;
			}
			else if (l < 0)
			{
				if (r >= 0)
					return det;
				else
					s = -(l + r);
			}
			else
				return det;

			decimal tol = ERRBOUND3 * s;
			if (det >= tol || det <= -tol)
				return det;

			decimal p = by * cx + -cy * bx + ay * bx + -by * ax;
			decimal n = ay * cx + -cy * ax;
			return p - n;
		}
	}
}

public record GridMORA(ICoordinate StartPos, Altitude?[] MORA, int FileRecordNumber, int Cycle) : RecordLine(new string(' ', 3), "AS", FileRecordNumber, Cycle)
{
	public static new GridMORA Parse(string line)
	{
		Check(line, 0, 13, "S   AS       ");

		Coordinate startPos = new(line[13..20]);
		List<FlightLevel?> morae = new();

		Check(line, 20, 30, new string(' ', 10));

		for (int fcharpos = 30; fcharpos < 120; fcharpos += 3)
		{
			string mora = line[fcharpos..(fcharpos + 3)];
			morae.Add(mora == "UNK" ? null : new FlightLevel(int.Parse(mora)));
		}

		return new(startPos, morae.ToArray(), int.Parse(line[123..128]), int.Parse(line[128..132]));
	}
}

public record ControlledAirspace(string Client,
	string Region, string Center, AirspaceClass ASClass, char MultiCD, uint SequenceNumber,
	BoundarySegment Boundary, (Altitude? Lower, Altitude? Upper) VerticalBounds, string Name,
	int FileRecordNumber, int Cycle) : RecordLine(Client, "UC", FileRecordNumber, Cycle)
{
	public static new ControlledAirspace Parse(string line)
	{
		// HEADER
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "UC");
		string region = line[6..8];
		char airspaceType = line[8];
		string center = line[9..14];
		char sectionCode = line[14];
		char sectionSubCode = line[15];
		AirspaceClass airspaceClass = (AirspaceClass)line[16];
		CheckEmpty(line, 17, 19);
		char multiCD = line[19];
		uint seqNumber = uint.Parse(line[20..24]);
		// END HEADER

		// Primary record
		int continuationNumber = int.Parse(line[24].ToString());
		char level = line[25];
		char timeCd = line[26];
		char notam = line[27];
		CheckEmpty(line, 28, 30);

		BoundarySegment boundary = BoundarySegment.Parse(line[30..78]);

		CheckEmpty(line, 78, 81);
		Altitude? lowerLimit = GetAltitude(line[81..87]),
				  upperLimit = GetAltitude(line[87..93]);
		string name = line[93..123].TrimEnd();
		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, region, center, airspaceClass, multiCD, seqNumber, boundary, (lowerLimit, upperLimit), name, frn, cycle);
	}

	public static bool TryParse(string line, [NotNullWhen(true)] out ControlledAirspace? result)
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

	public enum BoundaryViaType
	{
		Circle = 0b0_001,
		GreatCircle = 0b0_010,
		RhumbLine = 0b0_011,
		CounterClockwiseArc = 0b0_100,
		ClockwiseArc = 0b0_101,
		ReturnToOrigin = 0b_1000,
		Continue = 0b_0000
	}

	public enum AirspaceClass
	{
		A = 'A',
		B,
		C,
		D,
		E,
		G = 'G'
	}

	private static BoundaryViaType GetBoundaryViaType(string boundaryVia)
	{
		if (boundaryVia.Length != 2)
			throw new ArgumentException("Boundary Via type must be two characters.");

		return boundaryVia[0] switch
		{
			'C' => BoundaryViaType.Circle,
			'G' => BoundaryViaType.GreatCircle,
			'H' => BoundaryViaType.RhumbLine,
			'L' => BoundaryViaType.CounterClockwiseArc,
			'R' => BoundaryViaType.ClockwiseArc,
			_ => throw new ArgumentException("The provided BDRY VIA code is invalid.", nameof(boundaryVia))
		} | boundaryVia[1] switch
		{
			' ' => BoundaryViaType.Continue,
			'E' => BoundaryViaType.ReturnToOrigin,
			_ => throw new ArgumentException("The provided BDRY VIA code is invalid.", nameof(boundaryVia))
		};
	}

	internal static Altitude? GetAltitude(string altitudeData) =>
		altitudeData[5] switch
		{
			'A' when altitudeData[0..3] == "GND" => new AltitudeAGL(0, null),
			'A' => new AltitudeAGL(int.Parse(altitudeData[0..5]), null),
			'M' when altitudeData[0..2] == "FL" => new FlightLevel(int.Parse(altitudeData[2..5])),
			'M' when altitudeData[0..5] == "UNLTD" => new FlightLevel(999),
			'M' => new AltitudeMSL(int.Parse(altitudeData[0..5])),
			' ' => null,
			_ => throw new NotImplementedException()
		};

	public abstract record BoundarySegment(BoundaryViaType BoundaryVia, Coordinate Vertex)
	{
		public static BoundarySegment Parse(string data) =>
			(BoundaryViaType)((int)GetBoundaryViaType(data[0..2]) & 0b0111) switch
			{
				BoundaryViaType.ClockwiseArc or BoundaryViaType.CounterClockwiseArc => BoundaryArc.Parse(data),
				BoundaryViaType.Circle => BoundaryCircle.Parse(data),
				BoundaryViaType.GreatCircle => BoundaryLine.Parse(data),
				BoundaryViaType.RhumbLine => BoundaryEuclidean.Parse(data),
				_ => throw new NotImplementedException()
			};
	}

	public record BoundaryArc(BoundaryViaType BoundaryVia, ICoordinate ArcOrigin, decimal ArcDistance, TrueCourse ArcBearing, Coordinate ArcVertex) : BoundarySegment(BoundaryVia, ArcVertex)
	{
		public static new BoundaryArc Parse(string data)
		{
			Coordinate arcFromPoint = new(data[2..21]);
			Coordinate arcOriginPoint = new(data[21..40]);
			TrueCourse bearing = new(decimal.Parse(data[44..48]) / 10);
			decimal distance = decimal.Parse(data[40..44]) / 10;
			Coordinate extrapPoint = arcOriginPoint.FixRadialDistance(bearing, distance);

			decimal tolerance = Math.Max(0.01m * distance, 0.05m);
			if (arcFromPoint.DistanceTo(extrapPoint) > tolerance)
				throw new ArgumentException("Arc point doesn't line up with fix/radial/distance from origin." + $" {arcFromPoint.DMS} -> {extrapPoint.DMS} > {tolerance: #0.0#}nmi");

			return new(GetBoundaryViaType(data[0..2]), arcOriginPoint, distance, bearing, arcFromPoint);
		}
	}

	public record BoundaryCircle(BoundaryViaType BoundaryVia, Coordinate Centerpoint, decimal Radius) : BoundarySegment(BoundaryVia, Centerpoint)
	{
		public static new BoundaryCircle Parse(string data)
		{
			BoundaryViaType bvt = GetBoundaryViaType(data[0..2]);
			if (!bvt.HasFlag(BoundaryViaType.Circle))
				throw new ArgumentException("Not a circle!");

			Coordinate center = new(data[21..40]);
			decimal radius = decimal.Parse(data[40..44]) / 10;

			return new(bvt, center, radius);
		}
	}

	public record BoundaryLine(BoundaryViaType BoundaryVia, Coordinate Vertex) : BoundarySegment(BoundaryVia, Vertex)
	{
		public static new BoundaryLine Parse(string data)
		{
			BoundaryViaType bvt = GetBoundaryViaType(data[0..2]);
			if (!bvt.HasFlag(BoundaryViaType.GreatCircle))
				throw new ArgumentException("Not a segment along a great circle!");

			return new(bvt, new Coordinate(data[2..21]));
		}
	}

	public record BoundaryEuclidean(BoundaryViaType BoundaryVia, Coordinate Vertex) : BoundarySegment(BoundaryVia, Vertex)
	{
		public static new BoundaryEuclidean Parse(string data)
		{
			BoundaryViaType bvt = GetBoundaryViaType(data[0..2]);
			if (bvt != BoundaryViaType.RhumbLine)
				throw new ArgumentException("Not a rhumb line!");

			return new(bvt, new(data[2..21]));
		}
	}
}

public record AirportMSA(string Client,
	string Airport, string Fix, char MultiCode, AirportMSA.MSASector[] Sectors,
	int FileRecordNumber, int Cycle) : RecordLine(Client, "PS", FileRecordNumber, Cycle)
{
	public static new AirportMSA Parse(string line)
	{
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "P ", "H ");

		string airport = line[6..10];
		string airportIcaoRegion = line[10..12];

		Check(line, 12, 13, "S");

		string centerFix = line[13..18].TrimEnd();
		string centerFixIcaoRegion = line[18..20];
		string centerFixType = line[20..22];

		char multiCode = line[22]; // Sometimes there's different MSAs for different procedures.

		CheckEmpty(line, 23, 38);
		Check(line, 38, 39, "0", " ");
		CheckEmpty(line, 39, 42);

		List<MSASector> sectors = new();
		for (int startIndex = 42; startIndex <= 108 && line[startIndex] != ' '; startIndex += 11)
			sectors.Add(MSASector.Parse(line[startIndex..(startIndex + 11)]));

		if (sectors.Count < 7)
			CheckEmpty(line, (sectors.Count * 11) + 42, 119);

		// Magnetic courses only. Could probably extend this to true pretty easily, Ijust don't know the other value.
		Check(line, 119, 120, "M");

		CheckEmpty(line, 120, 123);

		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, airport, centerFix, multiCode, sectors.ToArray(), frn, cycle);
	}

	public record MSASector(MagneticCourse AntiClockwiseLimit, MagneticCourse ClockwiseLimit, AltitudeRestriction Altitude, decimal Radius)
	{
		public static MSASector Parse(string data)
		{
			if (data.Length != 11)
				throw new ArgumentException("MSA sector must be 11 characters.", nameof(data));

			MagneticCourse anticlockwiseLimit = new(decimal.Parse(data[0..3]), null);
			MagneticCourse clockwiseLimit = new(decimal.Parse(data[3..6]), null);

			AltitudeRestriction msa = new(new AltitudeMSL(int.Parse(data[6..9]) * 100), null);
			decimal radius = decimal.Parse(data[9..11]);

			return new(anticlockwiseLimit, clockwiseLimit, msa, radius);
		}
	}
}

public record RestrictiveAirspace(string Client,
	string Region, string Designation, RestrictiveAirspace.RestrictionType Restriction, uint SequenceNumber,
	BoundarySegment Boundary, (Altitude? Lower, Altitude? Upper) VerticalBounds, string Name,
	int FileRecordNumber, int Cycle) : RecordLine(Client, "UR", FileRecordNumber, Cycle)
{
	public static new RestrictiveAirspace? Parse(string line)
	{
		// HEADER
		Check(line, 0, 1, "S");
		string client = line[1..4];
		Check(line, 4, 6, "UR");
		string region = line[6..8];
		RestrictionType restriction = (RestrictionType)line[8];
		string designation = line[9..19];
		char multiCD = line[19];
		uint seqNumber = uint.Parse(line[20..24]);
		// END HEADER

		// Primary record
		int continuationNumber = int.Parse(line[24].ToString());

		if (continuationNumber > 1)
			return null;

		char level = line[25];
		char timeCd = line[26];
		char notam = line[27];
		CheckEmpty(line, 28, 30);

		BoundarySegment boundary = BoundarySegment.Parse(line[30..78]);

		CheckEmpty(line, 78, 81);
		Altitude? lowerLimit = GetAltitude(line[81..87]),
				  upperLimit = GetAltitude(line[87..93]);
		string name = line[93..123].TrimEnd();
		int frn = int.Parse(line[123..128]);
		int cycle = int.Parse(line[128..132]);

		return new(client, region, designation, restriction, seqNumber, boundary, (lowerLimit, upperLimit), name, frn, cycle);
	}

	public enum RestrictionType
	{
		Alert = 'A',
		Caution = 'C',
		Danger = 'D',
		MOA = 'M',
		Prohibited = 'P',
		Restricted = 'R',
		Training = 'T',
		Warning = 'W',
		Unknown = 'U'
	}
}