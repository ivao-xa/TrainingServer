﻿namespace TrainingServer.Extensibility
{
	public interface IPlugin
	{
		string FriendlyName { get; }
		string Maintainer { get; }

		/// <summary>Invoked when an <see cref="IAircraft"/> is sent a message by a controller either over frequency or PM to check if the plugin should handle it.</summary>
		/// <param name="aircraftCallsign">The callsign of the relevant aircraft.</param>
		/// <param name="sender">The callsign of the message sender.</param>
		/// <param name="message">The message that was sent.</param>
		/// <returns><see langword="true"/> if <see langword="this"/> <see cref="IPlugin"/> wishes to handle the message, otherwise <see langword="false"/> to pass it to other plugins.</returns>
		bool CheckIntercept(string aircraftCallsign, string sender, string message);

		/// <summary>Invoked after <see cref="CheckIntercept(string, string, string)"/> returns true to handle the message.</summary>
		/// <param name="aircraft">The <see cref="IAircraft"/> being sent the message.</param>
		/// <param name="sender">The callsign of the message sender.</param>
		/// <param name="message">The message that was sent.</param>
		/// <returns>Optionally, the reply for the controller.</returns>
		string? MessageReceived(IAircraft aircraft, string sender, string message);
	}

	public interface IServerPlugin
	{
		string FriendlyName { get; }
		string Maintainer { get; }

		/// <summary>Invoked when an <see cref="IAircraft"/> is sent a message by a controller either over frequency or PM to check if the plugin should handle it.</summary>
		/// <param name="sender">The callsign of the message sender.</param>
		/// <param name="message">The message that was sent.</param>
		/// <returns><see langword="true"/> if <see langword="this"/> <see cref="IPlugin"/> wishes to handle the message, otherwise <see langword="false"/> to pass it to other plugins.</returns>
		bool CheckIntercept(string sender, string message);

		/// <summary>Invoked after <see cref="CheckIntercept(string, string)"/> returns true to handle the message.</summary>
		/// <param name="server">The <see cref="IServer"/> being sent the message.</param>
		/// <param name="sender">The callsign of the message sender.</param>
		/// <param name="message">The message that was sent.</param>
		/// <returns>Optionally, the reply for the controller.</returns>
		string? MessageReceived(IServer server, string sender, string message);
	}
}
