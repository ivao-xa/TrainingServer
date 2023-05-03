using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIFPReader;

[JsonConverter(typeof(CourseJsonConverter))]
public abstract record Course : IProcedureVia
{
	public decimal Degrees { get; init; }

	public Course(decimal degrees) =>
		Degrees = (degrees is 360 or 0) ? 360 : ((degrees + 360) % 360);

	public abstract MagneticCourse ToMagnetic(decimal? variation);
	public abstract TrueCourse ToTrue();

	public abstract TrueCourse GetTrueCourse(Coordinate position, Course currentCourse, TimeSpan refreshRate, bool onGround);

	[JsonIgnore]
	public Course Reciprocal => this with { Degrees = (Degrees + 179) % 360 + 1 };

	private const decimal DEG_TO_RAD = (decimal)(Math.Tau / 360);

	[JsonIgnore]
	public decimal Radians => Degrees * DEG_TO_RAD;

	public static bool operator <(Course left, Course right) => left.ToTrue().Degrees < right.ToTrue().Degrees;
	public static bool operator >(Course left, Course right) => left.ToTrue().Degrees > right.ToTrue().Degrees;

	public static Course operator -(Course left, decimal right) =>
		left with { Degrees = left.Degrees - right };
	public static Course operator +(Course left, decimal right) =>
		left with { Degrees = left.Degrees + right };

	/// <summary>
	/// Gets the angle relative to another course.
	/// </summary>
	/// <returns>The angle in degrees, positive is clockwise.</returns>
	[DebuggerStepThrough]
	public decimal Angle(Course other)
	{
		decimal tc1 = ToTrue().Degrees, tc2 = other.ToTrue().Degrees;

		if (Math.Abs(tc1 - tc2) < 0.001m)
			return 0;

		if (tc1 > tc2)
		{
			decimal clockwise = (tc2 + 360) - tc1;
			decimal antiClockwise = tc1 - tc2;

			if (antiClockwise < clockwise)
				return -antiClockwise;
			else
				return clockwise;
		}
		else
		{
			decimal clockwise = tc2 - tc1;
			decimal antiClockwise = (tc1 + 360) - tc2;

			if (antiClockwise < clockwise)
				return -antiClockwise;
			else
				return clockwise;
		}
	}

	public override string ToString() => Degrees.ToString("000");

	public class CourseJsonConverter : JsonConverter<Course>
	{
		public override Course? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartArray)
				throw new JsonException();

			reader.Read();
			decimal deg = reader.GetDecimal();
			reader.Read();
			decimal? var =
				reader.TokenType == JsonTokenType.Null
				? null
				: reader.GetDecimal();
			reader.Read();

			if (reader.TokenType != JsonTokenType.EndArray)
				throw new JsonException();

			return var switch
			{
				decimal d => new MagneticCourse(deg, d),
				null => new TrueCourse(deg)
			};
		}

		public override void Write(Utf8JsonWriter writer, Course value, JsonSerializerOptions options)
		{
			MagneticCourse mv = value.ToMagnetic(null);
			writer.WriteStartArray();
			writer.WriteNumberValue(decimal.Round(mv.Degrees, 2));
			if (mv.Variation is null)
				writer.WriteNullValue();
			else
				writer.WriteNumberValue(decimal.Round(mv.Variation.Value, 2));
			writer.WriteEndArray();
		}
	}
}

public record MagneticCourse : Course
{
	public decimal? Variation { get; init; }

	public MagneticCourse(decimal Degrees, decimal? Variation) : base(Degrees) =>
		this.Variation = Variation;

	public void Deconstruct(out decimal Degrees, out decimal? Variation) =>
		(Degrees, Variation) = (this.Degrees, this.Variation);

	public override MagneticCourse ToMagnetic(decimal? variation) =>
		variation is null
		? new(this)
		: Variation is null
			? new(Degrees, variation)
			: ToTrue().ToMagnetic(variation);

	public override TrueCourse ToTrue() =>
		Variation is null
		? throw new Exception("Cannot convert magnetic to true course unless variation known")
		: new(Degrees - Variation.Value);

	public override TrueCourse GetTrueCourse(Coordinate position, Course currentCourse, TimeSpan refreshRate, bool onGround) =>
		IProcedureVia.TurnTowards(currentCourse, ToTrue(), refreshRate, onGround);

	public override string ToString() => base.ToString();
}

public record TrueCourse : Course
{
	public TrueCourse(decimal Degrees) : base(Degrees) { }

	public void Deconstruct(out decimal Degrees) =>
		Degrees = this.Degrees;

	public override MagneticCourse ToMagnetic(decimal? variation) =>
		new(Degrees + variation ?? 0, variation);

	public override TrueCourse ToTrue() => new(Degrees);

	public override TrueCourse GetTrueCourse(Coordinate position, Course currentCourse, TimeSpan refreshRate, bool onGround) =>
		IProcedureVia.TurnTowards(currentCourse, this, refreshRate, onGround);

	public override string ToString() => base.ToString();
}