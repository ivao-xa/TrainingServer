using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using TrainingServer.Extensibility;
using TrainingServer.Networking;

namespace TrainingServer;

public partial class FrmMain : Form
{
	public FrmMain()
	{
		InitializeComponent();

		Text = "Training Server (Disconnected)";
		BtnStart.Enabled = true;
		server = new();
		manager = new(server);
	}

	WebsocketMonitor? socket;
	Transcoder? transcoder;
	readonly PluginManager manager;
	readonly Server server;
	Guid guid;

	private void BtnStart_Click(object sender, EventArgs e)
	{
		switch (BtnStart.Text)
		{
			case "Connect":
				BtnStart.Enabled = false;

				Task.Run(ServerConnectedAsync);
				break;

			case "Disconnect":
				Task.Run(async () => await socket!.DisposeAsync(WebSocketCloseStatus.NormalClosure, "Good day!"));
				BtnStart.Text = "Connect";
				Text = "Training Server (Disconnected)";
				break;
		}
	}

	private async Task ServerConnectedAsync()
	{
		transcoder = new();
		ClientWebSocket sockClient = new();

		try
		{
			// Connect to the server.
			await sockClient.ConnectAsync(new("ws://127.0.0.1:5031/connect"), CancellationToken.None);
			socket = new(sockClient);
			_ = socket.MonitorAsync();

			// Get your GUID.
			string[] rsaParamElems = (await socket.InterceptNextTextAsync()).Split('|');
			if (rsaParamElems.Length != 3)
				return;

			guid = Guid.Parse(rsaParamElems[0]);
			Invoke(() => Text = $"Training Server (Connected: {guid})");
			transcoder.LoadAsymmetricKey(new() { Modulus = Convert.FromBase64String(rsaParamElems[1]), Exponent = Convert.FromBase64String(rsaParamElems[2]) });

			// Setup the encrypted tunnel.
			byte[] symKey = Aes.Create().Key;
			transcoder.RegisterKey(guid, symKey);

			byte[] symKeyCrypt = transcoder.AsymmetricEncrypt(symKey);
			await Task.Delay(100);
			await socket.SendAsync(symKeyCrypt);

			if (transcoder.SecureUnpack(await socket.InterceptNextTextAsync()).Data is not JsonArray ja || ja.Any())
				// Tunnel establishment handshake failed. Purge the server.
				await socket.DisposeAsync(WebSocketCloseStatus.ProtocolError, "Handshake failed.");

			await Task.Delay(100);
			await socket.SendAsync(transcoder.SecurePack(guid, guid, Array.Empty<object>()));

			// Send your server name.
			await Task.Delay(100);
			await socket.SendAsync(transcoder.SecurePack(guid, guid, $"{Environment.UserName}'s Server"));

			// Update the UI.
			BtnStart.Invoke(() =>
			{
				BtnStart.Text = "Disconnect";
				BtnStart.Enabled = true;
			});

			socket.OnTextMessageReceived += async (msg) =>
			{
				var (date, recipient, text) = transcoder.SecureUnpack(msg);

				if (text.Deserialize<NetworkMessage>() is NetworkMessage netmsg)
					await server.MessageReceivedAsync(netmsg);
			};

			server.OnTextMessageReceived += async txt =>
			{
				string sender, recipient;

				switch (server.ResolveGuid(txt.To))
				{
					case Controller c:
						sender = c.Metadata.Callsign;
						break;

					case Aircraft ac:
						sender = ac.Metadata.Callsign;
						break;

					default:
						return;
				}

				if (FrequencyGuidRegex().IsMatch(txt.To.ToString()))
					recipient =
						decimal.Parse(FrequencyGuidRegex().Match(txt.To.ToString()).Groups[1].Value)
							   .ToString("000.00#");
				else if (server.ResolveGuid(txt.To) is Controller c)
					recipient = c.Metadata.Callsign;
				else if (server.ResolveGuid(txt.To) is Aircraft ac)
					recipient = ac.Metadata.Callsign;
				else
					// Couldn't find the Guid.
					return;

				await manager.ProcessTextMessageAsync(sender, recipient, txt.Message);
			};

			server.OnAircraftAdded += g => transcoder.RegisterSecondaryRecipient(g, guid);

			server.OnAircraftUpdated += Server_OnAircraftUpdated;

			await PollPluginsAsync();
		}
		catch (WebSocketException) { }

		await socket!.DisposeAsync(WebSocketCloseStatus.NormalClosure, "Good day!");
	}

	private async void Server_OnAircraftUpdated(AircraftUpdate[] delta)
	{
		if (socket is null || transcoder is null || !socket.IsConnected)
			return;

		foreach (var update in delta)
			await socket.SendAsync(transcoder.SecurePack(guid, guid, update));
	}

	private async Task PollPluginsAsync()
	{
		DateTimeOffset lastRun = DateTimeOffset.Now;

		while (socket?.IsConnected ?? false)
		{
			TimeSpan delta = DateTimeOffset.Now - lastRun;
			lastRun = DateTimeOffset.Now;

			await manager.TickAsync(delta);
			server.CommitBatch();
		}
	}

	private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
	{
		if (BtnStart.Text == "Disconnect")
			socket?.DisposeAsync(WebSocketCloseStatus.NormalClosure, "Good day!").AsTask().RunSynchronously();
	}

	[GeneratedRegex(@"^(\d{8})-0000-0000-0000-000000000000$", RegexOptions.Compiled | RegexOptions.Singleline)]
	private static partial Regex FrequencyGuidRegex();
}
