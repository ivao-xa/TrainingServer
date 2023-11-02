using System;

namespace TrainingServer.Extensibility;

/// <summary>An immutable representation of an aircraft at a point in time.</summary>
/// <param name="Time">The time at which the aircraft information is valid.</param>
/// <param name="Position">The state of the aircraft at the given <paramref name="Time"/>.</param>
/// <param name="Movement">The current movement information of the aircraft.</param>
public record Aircraft(DateTimeOffset Time, FlightData Metadata, AircraftSnapshot Position, AircraftMotion Movement)
{
	public Aircraft(FlightData metadata, AircraftSnapshot position, AircraftMotion movement) : this(DateTimeOffset.Now, metadata, position, movement) { }

	public Aircraft Extrapolate(DateTimeOffset targetTime) => new(targetTime, Metadata, Movement.Apply(Position, targetTime - Time), Movement);

	public override int GetHashCode() => HashCode.Combine(Metadata.Callsign, Metadata.Type);
}

/// <summary>The point-in-time frozen state of an aircraft containing no movement information.</summary>
/// <param name="Heading">The heading of the aircraft in degrees from true North.</param>
/// <param name="Altitude">The altitude of the aircraft in feet above the WGS-84 reference spheroid.</param>
/// <param name="Position">The coordinates of the aircraft on the WGS-84 reference spheroid.</param>
public record struct AircraftSnapshot(float Heading, int Altitude, Coordinate Position, Squawk Squawk) { }

/// <summary>The in-motion state of the aircraft containing no time or planning information.</summary>
/// <remarks>For point-in-time information, use <seealso cref="AircraftSnapshot"/>.</remarks>
/// <param name="Speed">The component of the aircraft's velocity (in knots) parallel to the surface of the WGS-84 spheroid.</param>
/// <param name="ClimbRate">The component of the aircraft's velocity (in feet per second) normal to the surface of the WGS-84 spheroid.</param>
/// <param name="TurnRate">The rate of change (in degrees per second; + is clockwise) in aircraft heading.</param>
public record struct AircraftMotion(uint Speed, int ClimbRate, float TurnRate)
{
	public AircraftSnapshot Apply(AircraftSnapshot source, TimeSpan duration)
	{
		if (TurnRate != 0)
			throw new NotImplementedException();

		return source with {
			Heading = (float)duration.TotalSeconds * TurnRate + source.Heading,
			Altitude = source.Altitude + (int)(ClimbRate * duration.TotalSeconds),
			Position = source.Position.FixRadialDistance(source.Heading, (float)duration.TotalHours * Speed)
		};
	}
}

/// <summary>Metadata about a flight.</summary>
/// <param name="Callsign">The aircraft's callsign.</param>
/// <param name="Origin">The departure airport of the flight.</param>
/// <param name="Destination">The arrival airport of the flight.</param>
/// <param name="Rules">The rules under which the flight is conducted.</param>
/// <param name="Type">The type of aircraft being flown.</param>
/// <param name="Route">The filed route of flight.</param>
/// <param name="Remarks">Pilot-supplied remarks for the flight.</param>
public record struct FlightData(string Callsign, string Origin, string Destination, FlightData.FlightRules Rules, string Type, string Route, string Remarks)
{ 
	public enum FlightRules
	{
		VFR,
		IFR,
		Y,
		Z
	}
}

/// <summary>Squawk code and mode.</summary>
/// <param name="Code">The code on which the aircraft's transponder is responding. Undefined when <paramref name="Mode"/> is <see cref="SquawkMode.Standby"/>.</param>
/// <param name="Mode">The SSR state of the aircraft's transponder.</param>
public record struct Squawk(ushort Code, Squawk.SquawkMode Mode)
{
	public enum SquawkMode
	{
		Standby,
		On,
		Altitude
	}
}