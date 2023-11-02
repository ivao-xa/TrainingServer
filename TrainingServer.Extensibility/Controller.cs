using System;
using System.Text.Json.Serialization;

namespace TrainingServer.Extensibility;

public record Controller(DateTimeOffset Time, ControllerData Metadata, ControllerSnapshot Position) { }

public record struct ControllerSnapshot(Coordinate[] RadarAntennae) { }

public record struct ControllerData(string Facility, ControllerData.Level Type, string? Discriminator = null)
{
	[JsonIgnore]
	public readonly string Callsign => $"{Facility}{(Discriminator is string s ? "_" + s : "")}_{Enum.GetName(Type)}";

	public override readonly string ToString() => Callsign;
		

	public enum Level
	{
		DEL,
		GND,
		TWR,
		APP,
		DEP,
		CTR,
		FSS
	}
}