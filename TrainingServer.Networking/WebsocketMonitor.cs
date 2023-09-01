namespace TrainingServer.Networking;

using System.Net.WebSockets;
using System.Text;

/// <summary>
/// Provides a convenient, event-based wrapper around a <see cref="WebSocket"/>.
/// </summary>
public sealed class WebsocketMonitor : IAsyncDisposable
{
	public event Action<string>? OnTextMessageReceived;
	public event Action<byte[]>? OnBinaryMessageReceived;

	public bool IsConnected => _connection.CloseStatus is not null && _connection.State is not WebSocketState.Closed;

	private Action<string>? InterceptSingleText;
	private Action<byte[]>? InterceptSingleBinary;

	readonly WebSocket _connection;
	readonly CancellationTokenSource _kill = new();

	/// <summary>
	/// Wraps a <see cref="WebSocket"/>.
	/// </summary>
	public WebsocketMonitor(WebSocket socket)
	{
		_connection = socket;
	}

	/// <summary>
	/// Wraps a <see cref="ClientWebSocket"/>.
	/// </summary>
	public WebsocketMonitor(ClientWebSocket socket)
	{
		if (socket.State != WebSocketState.Open)
			throw new ArgumentException();

		_connection = (WebSocket)typeof(ClientWebSocket).GetProperty("ConnectedWebSocket", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(socket)!;
	}

	/// <summary>Begins listening to the <see cref="WebSocket"/>.</summary>
	public async Task MonitorAsync()
	{
		CancellationToken cancellationToken = _kill.Token;
		var buffer = new byte[1024 * 4];
		string messageText = "";
		List<byte> messageBuffer = new();
		var receiveResult = await _connection.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

		while (!cancellationToken.IsCancellationRequested && !receiveResult.CloseStatus.HasValue)
		{
			if (receiveResult.MessageType == WebSocketMessageType.Text || !string.IsNullOrEmpty(messageText))
			{
				messageText += Encoding.UTF8.GetString(buffer).TrimEnd('\0');

				if (receiveResult.EndOfMessage)
				{
					if (InterceptSingleText is not null)
						InterceptSingleText.Invoke(messageText);
					else if (OnTextMessageReceived is not null)
						OnTextMessageReceived.Invoke(messageText);
					else
					{
						while (InterceptSingleText is null)
							Thread.Yield();

						InterceptSingleText.Invoke(messageText);
					}

					InterceptSingleText = null;

					messageText = "";
				}
			}
			else
			{
				messageBuffer.AddRange(buffer);

				if (receiveResult.EndOfMessage)
				{
					if (InterceptSingleBinary is not null)
						InterceptSingleBinary.Invoke(messageBuffer.ToArray());
					else if (OnBinaryMessageReceived is not null)
						OnBinaryMessageReceived.Invoke(messageBuffer.ToArray());
					else
					{
						while (InterceptSingleBinary is null)
							Thread.Yield();

						InterceptSingleBinary.Invoke(messageBuffer.ToArray());
					}

					InterceptSingleBinary = null;

					messageBuffer.Clear();
				}
			}

			receiveResult = await _connection.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
		}

		await DisposeAsync();
	}

	/// <summary>
	/// Sends a text message to the connected client.
	/// </summary>
	/// <param name="data">The message to send.</param>
	public Task SendAsync(string data) => SendAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text);


	/// <summary>
	/// Sends a message to the connected client.
	/// </summary>
	/// <param name="data">The message to send.</param>
	/// <param name="type">The type of message.</param>
	public async Task SendAsync(byte[] data, WebSocketMessageType type = WebSocketMessageType.Binary) =>
		await _connection.SendAsync(new ArraySegment<byte>(data), type, true, _kill.Token);

	/// <summary>Intercepts the next received text message.</summary>
	/// <returns>The intercepted message.</returns>
	public async Task<string> InterceptNextTextAsync()
	{
		SemaphoreSlim breaker = new(0);
		string result = "";

		InterceptSingleText = m => { result = m; breaker.Release(); };
		await breaker.WaitAsync();

		return result;
	}

	/// <summary>Intercepts the next received binary message.</summary>
	/// <returns>The intercepted message.</returns>
	public async Task<byte[]> InterceptNextBinaryAsync()
	{
		SemaphoreSlim breaker = new(0);
		byte[] result = Array.Empty<byte>();

		InterceptSingleBinary = m => { result = m; breaker.Release(); };
		await breaker.WaitAsync();

		return result;
	}

	/// <summary>Disposes the current instance normally.</summary>
	public ValueTask DisposeAsync() => DisposeAsync(WebSocketCloseStatus.NormalClosure, "Connection terminated normally.");

	/// <summary>Disposes the current instance with the provided <paramref name="closeStatus"/> and <paramref name="message"/>.</summary>
	/// <param name="closeStatus">The error code with which the socket will be closed.</param>
	/// <param name="message">The reason for closure to be provided to the remote client.</param>
	public async ValueTask DisposeAsync(WebSocketCloseStatus closeStatus, string message)
	{
		_kill.Cancel(false);

		if (_connection.CloseStatus is null && _connection.State is not WebSocketState.CloseSent and not WebSocketState.Closed and not WebSocketState.Aborted)
			await _connection.CloseAsync(closeStatus, message, CancellationToken.None);
	}
}