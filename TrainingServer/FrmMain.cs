using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using TrainingServer.Networking;

namespace TrainingServer;

public partial class FrmMain : Form
{
	public FrmMain()
	{
		InitializeComponent();


		Text = "Training Server (Disconnected)";
		BtnStart.Enabled = true;
	}

	WebsocketMonitor? socket;
	Transcoder? transcoder;
	Guid guid;

	private void BtnStart_Click(object sender, EventArgs e)
	{
		switch (BtnStart.Text)
		{
			case "Connect":
				BtnStart.Enabled = false;

				Task.Run(async () =>
				{
					transcoder = new();
					ClientWebSocket sockClient = new();

					try
					{
						await sockClient.ConnectAsync(new("ws://127.0.0.1:5031/connect"), CancellationToken.None);
						socket = new(sockClient);
						Task blocker = socket.MonitorAsync();

						string[] rsaParamElems = (await socket.InterceptNextTextAsync()).Split('|');
						if (rsaParamElems.Length != 3)
							return;

						guid = Guid.Parse(rsaParamElems[0]);
						Invoke(() => Text = $"Training Server (Connected: {guid})");
						transcoder.LoadAsymmetricKey(new() { Modulus = Convert.FromBase64String(rsaParamElems[1]), Exponent = Convert.FromBase64String(rsaParamElems[2]) });

						byte[] symKey = Aes.Create().Key;
						transcoder.RegisterKey(guid, symKey);

						byte[] symKeyCrypt = transcoder.AsymmetricEncrypt(symKey);
						await Task.Delay(100);
						await socket.SendAsync(symKeyCrypt);

						if (transcoder.SecureUnpack(await socket.InterceptNextTextAsync()).Data is not JsonArray ja || ja.Any())
							// Tunnel establishment handshake failed. Purge the server.
							await socket.DisposeAsync(WebSocketCloseStatus.ProtocolError, "Handshake failed.");

						await Task.Delay(100);
						await socket.SendAsync(transcoder.SecurePack(guid, Array.Empty<object>()));

						BtnStart.Invoke(() =>
						{
							BtnStart.Text = "Disconnect";
							BtnStart.Enabled = true;
						});

						socket.OnTextMessageReceived += (msg) =>
						{
							var (date, text) = transcoder.SecureUnpack(msg);
						};

						await blocker;
					}
					catch (WebSocketException) { }

					await socket!.DisposeAsync(WebSocketCloseStatus.NormalClosure, "Good day!");
				});
				break;

			case "Disconnect":
				Task.Run(async () => await socket!.DisposeAsync(WebSocketCloseStatus.NormalClosure, "Good day!"));
				BtnStart.Text = "Connect";
				Text = "Training Server (Disconnected)";
				break;
		}
	}
}
