using System.Text.Json.Serialization;

using TrainingServer.Extensibility;

namespace TrainingServer.Networking;

public record struct ServerInfo(Guid Guid, string ReadableName) { }

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(AircraftUpdate), '%')]
[JsonDerivedType(typeof(ControllerUpdate), '@')]
[JsonDerivedType(typeof(AuthoritativeUpdate), '*')]
[JsonDerivedType(typeof(TextMessage), '$')]
[JsonDerivedType(typeof(ChannelMessage), '#')]
[JsonDerivedType(typeof(KillMessage), '!')]
public abstract record NetworkMessage() { }

public abstract record UserUpdate(Guid User, UserUpdate.UpdatedField Update) : NetworkMessage
{
	[Flags]
	public enum UpdatedField
	{
		None = 0,
		Delete = 1 << 0,
		Metadata = 1 << 1,
		State = 1 << 2,
		Movement = 1 << 3
	}
}

/// <summary>Packed update information about a state change to an aircraft.</summary>
public record AircraftUpdate(Guid Aircraft, UserUpdate.UpdatedField Update, FlightData? Metadata, AircraftSnapshot? State, AircraftMotion? Movement) : UserUpdate(Aircraft, Update)
{
	public AircraftUpdate(Guid aircraft, FlightData? metadata = null, AircraftSnapshot? state = null, AircraftMotion? movement = null) : this(aircraft, (metadata is null ? 0 : UpdatedField.Metadata) | (state is null ? 0 : UpdatedField.State) | (movement is null ? 0 : UpdatedField.Movement), metadata, state, movement) { }

	public AircraftUpdate(Guid aircraft, Aircraft info) : this(aircraft, info.Metadata, info.Position, info.Movement) { }

	/// <summary>Combines two <see cref="AircraftUpdate"/>s, using the <see cref="Guid"/> from the left.</summary>
	public static AircraftUpdate operator +(AircraftUpdate left, AircraftUpdate right)
	{
		if (right.Update.HasFlag(UpdatedField.Delete))
			return new(left.Aircraft, UpdatedField.Delete, null, null, null);

		if (left.Update.HasFlag(UpdatedField.Delete))
			return right with { Aircraft = left.Aircraft };

		AircraftUpdate newUpdate = left;

		if (right.Update.HasFlag(UpdatedField.Metadata))
			newUpdate = newUpdate with { Metadata = right.Metadata, Update = newUpdate.Update | UpdatedField.Metadata };

		if (right.Update.HasFlag(UpdatedField.State))
			newUpdate = newUpdate with { State = right.State, Update = newUpdate.Update | UpdatedField.State };

		if (right.Update.HasFlag(UpdatedField.Movement))
			newUpdate = newUpdate with { Movement = right.Movement, Update = newUpdate.Update | UpdatedField.Movement  };

		return newUpdate;
	}

	/// <summary>Updates an <see cref="Extensibility.Aircraft"/> from the given <see cref="AircraftUpdate"/>.</summary>
	public static Aircraft operator +(Aircraft ac, AircraftUpdate update)
	{
		if (update.Update.HasFlag(UpdatedField.Delete))
			throw new ArgumentException("Cannot delete an aircraft with application.", nameof(update));

		if (update.Update.HasFlag(UpdatedField.Metadata))
			ac = ac with { Metadata = update.Metadata!.Value };

		if (update.Update.HasFlag(UpdatedField.State))
			ac = ac with { Position = update.State!.Value };

		if (update.Update.HasFlag(UpdatedField.Movement))
			ac = ac with { Movement = update.Movement!.Value };

		return ac;
	}

	/// <summary>Finds the difference between two <see cref="Extensibility.Aircraft"/>.</summary>
	public static AircraftUpdate Difference(Guid aircraftId, Aircraft from, Aircraft to)
	{
		AircraftUpdate newUpdate = new(aircraftId);

		if (from.Metadata != to.Metadata)
			newUpdate = newUpdate with { Metadata = to.Metadata, Update = newUpdate.Update | UpdatedField.Metadata };

		if (from.Position != to.Position)
			newUpdate = newUpdate with { State = to.Position, Update = newUpdate.Update | UpdatedField.State };

		if (from.Movement != to.Movement)
			newUpdate = newUpdate with { Movement = to.Movement, Update = newUpdate.Update | UpdatedField.Movement };

		return newUpdate;
	}

	public Aircraft ToAircraft() => new(Metadata ?? new(), State ?? new(), Movement ?? new());

	public override int GetHashCode() => Aircraft.GetHashCode();
}

/// <summary>Packed update information about a state change to a controller.</summary>
public record ControllerUpdate(Guid Controller, UserUpdate.UpdatedField Update, ControllerData? Metadata, ControllerSnapshot? State) : UserUpdate(Controller, Update)
{
	public ControllerUpdate(Guid controller, ControllerData? metadata = null, ControllerSnapshot? state = null) : this(controller, (metadata is null ? 0 : UpdatedField.Metadata) | (state is null ? 0 : UpdatedField.State), metadata, state) { }

	public ControllerUpdate(Guid controller, Controller info) : this(controller, info.Metadata, info.Position) { }
}

public record AuthoritativeUpdate(Guid Recipient, UserUpdate[] Updates) : UserUpdate(Recipient, UpdatedField.Metadata | UpdatedField.State | UpdatedField.Movement) { }

public abstract record ControlMessage(Guid To) : NetworkMessage { }

public record TextMessage(Guid From, Guid To, string Message) : ControlMessage(To) { }

public record ChannelMessage(Guid From, decimal Frequency, string Message) : TextMessage(From, Guid.Parse($"{Frequency * 1000:00000000}-0000-0000-0000-000000000000"), Message) { }

public record KillMessage(Guid Victim) : ControlMessage(Victim) { }