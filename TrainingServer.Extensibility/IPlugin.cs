using System;
using System.Threading.Tasks;

namespace TrainingServer.Extensibility;

/// <summary>The basic plugin interface. Implement this to have your plugin loaded by the server.</summary>
public interface IPlugin
{
	/// <summary>The name of the plugin.</summary>
	public string Name { get; }

	/// <summary>A brief, one-line description of the plugin.</summary>
	public string Description { get; }

	/// <summary>The plugin maintainer. Use format "Name (VID)".</summary>
	public string Maintainer { get; }

	public string ToString() => $"{Name} ({Description})";

	/// <summary>Called whenever a text message is received.</summary>
	/// <param name="sender">The sender of the message.</param>
	/// <param name="recipient">The recipient of the message.</param>
	/// <param name="message">The message that was sent.</param>
	public Task ProcessTextMessageAsync(string sender, string recipient, string message);

	/// <summary>Called by the server as frequently as possible as part of the main pump.</summary>
	/// <param name="delta">The time elapsed since the last call.</param>
	public Task TickAsync(TimeSpan delta);
}
