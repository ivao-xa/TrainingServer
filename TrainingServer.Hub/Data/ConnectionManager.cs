namespace TrainingServer.Hub.Data;

using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json.Nodes;

using TrainingServer.Networking;

/// <summary>
/// Manages websocket connections received by the server.
/// </summary>
public class ConnectionManager
{
	private readonly ConcurrentDictionary<Guid, WebsocketMonitor> _monitors = new();
	private readonly ConcurrentDictionary<Guid, Guid> _connectedServers = new();
	private readonly Transcoder _transcoder = new();

	/// <summary>Accepts an incoming client websocket connection and establishes a secure tunnel.</summary>
	/// <param name="serverId">The server that the websocket was established against.</param>
	/// <param name="socket">The accepted <see cref="WebSocket"/>.</param>
	public async Task AcceptClientAsync(string serverId, WebSocket socket)
	{
		Guid guid = Guid.NewGuid();
		WebsocketMonitor monitor = new(socket);

		_monitors.TryAdd(guid, monitor);

		if (Guid.Parse(serverId) is Guid server && _monitors.ContainsKey(server))
		{
			try
			{
				// Perform the handshake to setup encryption.
				Task blocker = monitor.MonitorAsync();
				await SetupAsync(guid);

				monitor.OnTextMessageReceived += async msg =>
				{
					var (sent, data) = _transcoder.SecureUnpack(msg);
					await monitor.SendAsync($"({sent:s} -> {DateTime.UtcNow:s}) {serverId} recieved: {data.ToJsonString()}");
				};

				await blocker;
			}
			catch (ArgumentException ex)
			{
				// Remote client is non-compliant. Purge it.
				_transcoder.TryUnregister(guid);
				if (_monitors.TryRemove(guid, out var sock))
					await sock.DisposeAsync(WebSocketCloseStatus.InvalidPayloadData, ex.Message);
			}
		}

		if (_monitors.TryRemove(guid, out var m))
		{
			if (_monitors.ContainsKey(server))
				await m.DisposeAsync(WebSocketCloseStatus.EndpointUnavailable, "The requested server is not available.");
			else
				await m.DisposeAsync();
		}

		_connectedServers.TryRemove(guid, out var _);
	}

	/// <summary>Accepts an incoming server websocket connection and establishes a secure tunnel.</summary>
	/// <param name="socket">The accepted <see cref="WebSocket"/>.</param>
	public async Task AcceptServerAsync(WebSocket socket)
	{
		Guid guid = Guid.NewGuid();
		WebsocketMonitor monitor = new(socket);
		_monitors.TryAdd(guid, monitor);

		try
		{
			// Perform the handshake to setup encryption.
			Task blocker = monitor.MonitorAsync();
			await SetupAsync(guid);

			monitor.OnTextMessageReceived += async msg =>
			{
				var (sent, data) = _transcoder.SecureUnpack(msg);
				await monitor.SendAsync(_transcoder.SecurePack(guid, $"({sent:ss;fff} -> {DateTime.UtcNow:ss;fff}) {guid} sent: {data.ToJsonString()}"));
			};

			await blocker;
		}
		catch (ArgumentException ex)
		{
			// Remote client is non-compliant. Purge it.
			_transcoder.TryUnregister(guid);
			if (_monitors.TryRemove(guid, out var sock))
				await sock.DisposeAsync(WebSocketCloseStatus.ProtocolError, ex.Message);
		}
		catch (WebSocketException) { }

		// Disconnect all the connected clients.
		Task.WaitAll(_connectedServers.Where(kvp => kvp.Value == guid).Select(async kvp => await _monitors[kvp.Key].DisposeAsync(WebSocketCloseStatus.EndpointUnavailable, "The server is shutting down.")).ToArray());

		if (_monitors.TryRemove(guid, out var m))
			await m.DisposeAsync();
	}

	private async Task SetupAsync(Guid guid)
	{
		// Establish a tunnel.
		var socket = _monitors[guid];
		var key = _transcoder.GetAsymmetricKey();
		_ = socket.SendAsync(guid.ToString() + '|' + Convert.ToBase64String(key.Modulus!) + '|' + Convert.ToBase64String(key.Exponent!));

		byte[] symKeyCrypt = (await socket.InterceptNextBinaryAsync())[..256];
		_transcoder.RegisterKey(guid, _transcoder.AsymmetricDecrypt(symKeyCrypt));

		await Task.Delay(100);
		// Send an encrypted handshake.
		_ = _monitors[guid].SendAsync(_transcoder.SecurePack(guid, Array.Empty<object>()));

		if (_transcoder.SecureUnpack(await socket.InterceptNextTextAsync()).Data is not JsonArray ja || ja.Any())
		{
			// Tunnel establishment handshake failed. Purge the client.
			_transcoder.TryUnregister(guid);
			if (_monitors.TryRemove(guid, out var sock))
				await sock.DisposeAsync(WebSocketCloseStatus.ProtocolError, "Handshake failed.");
		}
	}
}
