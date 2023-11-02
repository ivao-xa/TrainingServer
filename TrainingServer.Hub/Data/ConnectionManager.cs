namespace TrainingServer.Hub.Data;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json.Nodes;

using TrainingServer.Networking;

/// <summary>
/// Manages websocket connections received by the server.
/// </summary>
public class ConnectionManager
{
	private readonly ConcurrentDictionary<Guid, WebsocketMonitor> _monitors = new();
	private readonly ConcurrentBag<ServerInfo> _servers = new();
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
					var (sent, recipient, data) = _transcoder.SecureUnpack(msg);

					if (_monitors.TryGetValue(recipient, out var rSock))
						await rSock.SendAsync(_transcoder.SecurePack(server, recipient, msg));
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
			_servers.Add(new(guid, _transcoder.SecureUnpack(await monitor.InterceptNextTextAsync()).Data.ToString()));

			monitor.OnTextMessageReceived += async msg =>
			{
				var (sent, recipient, data) = _transcoder.SecureUnpack(msg);

				if (recipient == guid)
					await Task.WhenAll(_monitors.Where(kvp => kvp.Key != guid).AsParallel().Select(kvp => kvp.Value.SendAsync(_transcoder.SecurePack(guid, recipient, msg))));
				else if (_monitors.TryGetValue(recipient, out var rSock))
					await rSock.SendAsync(_transcoder.SecurePack(guid, recipient, msg));
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

		if (_monitors.TryRemove(guid, out var m))
			await m.DisposeAsync();
	}

	public IEnumerable<ServerInfo> ListServers() => _servers.AsEnumerable();

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
		_ = _monitors[guid].SendAsync(_transcoder.SecurePack(guid, guid, Array.Empty<object>()));

		if (_transcoder.SecureUnpack(await socket.InterceptNextTextAsync()).Data is not JsonArray ja || ja.Any())
		{
			// Tunnel establishment handshake failed. Purge the client.
			_transcoder.TryUnregister(guid);
			if (_monitors.TryRemove(guid, out var sock))
				await sock.DisposeAsync(WebSocketCloseStatus.ProtocolError, "Handshake failed.");
		}
	}
}
