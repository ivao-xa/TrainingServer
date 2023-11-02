using System;
using System.Collections.Generic;

namespace TrainingServer.Extensibility;

/// <summary>A user-facing representation of a training server.</summary>
public interface IServer
{
	/// <summary>Adds a new <see cref="Aircraft"/> to the server.</summary>
	/// <param name="newAircraft">The <see cref="Aircraft"/> to add.</param>
	public Guid AddAircraft(Aircraft newAircraft);

	/// <summary>Updates an <see cref="Aircraft"/> on the server.</summary>
	/// <param name="newAircraft">The new <see cref="Aircraft"/> data to use.</param>
	/// <returns><see langword="true"/> if the aircraft was found and updated, else <see langword="false"/>.</returns>
	public bool UpdateAircraft(Guid aircraftId, Aircraft newData);

	/// <summary>Removes an existing <see cref="Aircraft"/> from the server.</summary>
	/// <param name="aircraftId">The <see cref="Guid"/> of the <see cref="Aircraft"/> to remove.</param>
	/// <returns><see langword="true"/> if the aircraft was found and removed, else <see langword="false"/>.</returns>
	public bool RemoveAircraft(Guid aircraftId);

	/// <summary>Removes an existing <see cref="Aircraft"/>(s) from the server based on their callsigns.</summary>
	/// <param name="callsign">The callsign of the <see cref="Aircraft"/> to remove.</param>
	/// <returns>The array of all <see cref="Aircraft"/> found and removed.</returns>
	public Guid[] RemoveAircraft(string callsign);

	/// <summary>Gets all <see cref="Aircraft"/> with the given callsign.</summary>
	/// <param name="callsign">The callsign of the <see cref="Aircraft"/> to find.</param>
	/// <returns>All <see cref="Aircraft"/> with the given callsign.</returns>
	public IDictionary<Guid, Aircraft> GetAircraftByCallsign(string callsign);

	/// <summary>All <see cref="Aircraft"/> known to the server.</summary>
	public IDictionary<Guid, Aircraft> Aircraft { get; }

	/// <summary>All <see cref="Controller"/>s known to the server.</summary>
	public IDictionary<Guid, Controller> Controllers { get; }
}
