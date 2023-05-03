using IVAN.FSD;
using IVAN.FSD.Protocol;

using System.Collections.Generic;

namespace TrainingServer;

internal class AtcManager
{
	public event EventHandler<TextMessage>? TextMessageReceived;
	public event EventHandler<InformationRequestMessage>? InfoRequestReceived;
	public string? Callsign => _atcInfo?.Source;

	internal AddATCMessage? AtcInfo { get => _atcInfo is null ? null : _atcInfo with { Password = "" }; }

	AddATCMessage? _atcInfo;
	readonly SocketLineStream _controller;
	readonly Dictionary<string, AdministrativeMessage?> _pendingHandoffs = new();

	public AtcManager(SocketLineStream sls, Server server, Action<AtcManager>? onConnected = null)
	{
		_controller = sls;

		Task.Run(async () => { try { await MonitorAsync(server, onConnected); } catch (TaskCanceledException) { } });
	}

	private async Task MonitorAsync(Server server, Action<AtcManager>? onConnected)
	{
		CancellationTokenSource cts = new();
		CancellationToken token = cts.Token;

		await foreach (string line in _controller.ReadAllLinesAsync(token))
		{
			switch (INetworkMessage.Parse(line))
			{
				case null:
					continue;

				case AddATCMessage aam:
					_atcInfo = aam;
					await SendAsync(new ServerVerificationMessage(aam.Destination, aam.Source, 0));
					break;

				case ClientVerificationMessage cvm when _atcInfo is not null:
					await SendAsync(new RegistrationInformationMessage(
						cvm.Destination, cvm.Source,
						RegistrationInformationMessage.ComputeSignature(cvm.Signature, int.Parse(_atcInfo.VID), ulong.Parse(cvm.Seed)),
						11, 12, System.Net.IPAddress.Loopback
					));
					await SendAsync(new TextMessage("SERVER", _atcInfo.Source, "IVAO <3 Trainers (training server by Wes - XA)"));
					onConnected?.Invoke(this);
					break;

				case AssumeControlMessage acm:
					await SendAsync(acm);
					break;

				case HandoffRequestMessage hrm:
					if (_pendingHandoffs.ContainsKey(hrm.Pilot))
					{
						_pendingHandoffs[hrm.Pilot] = new HandoffApproveMessage(hrm.Source, hrm.Destination, hrm.Pilot);
						await SendAsync(new HandoffApproveMessage(hrm.Destination, hrm.Source, hrm.Pilot));
					}
					else
						_ = Task.Run(async () => await SendAsync(await server.TransferAsync(hrm, token)), token);
					break;

				case TextMessage tm:
					_ = Task.Run(() => TextMessageReceived?.Invoke(this, tm));
					break;

				case AdministrativeMessage am when am is HandoffApproveMessage or HandoffRejectMessage:
					string pilot = am switch { HandoffApproveMessage ham => ham.Pilot, HandoffRejectMessage hrm => hrm.Pilot, _ => throw new Exception() };

					if (_pendingHandoffs.ContainsKey(pilot))
						_pendingHandoffs[pilot] = am;
					break;

				case ATCPositionUpdateMessage:
				case ATISCancelMessage:
				case ATISMessage:
					break;

				case InformationRequestMessage irm:
					_ = Task.Run(() => InfoRequestReceived?.Invoke(this, irm));
					break;

				case ClearanceMessage:
					break;

				default:
					System.Diagnostics.Debugger.Break();
					break;
			}
		}
	}

	public async Task DistributePositionAsync(PilotPositionUpdateMessage ppum) =>
		await SendAsync(ppum);
	public async Task DistributeDeletionAsync(DeletePilotMessage dpm) =>
		await SendAsync(dpm);
	public async Task DistributeCommunicationAsync(CommunicationMessage cm) =>
		await SendAsync(cm);

	public async Task SendTextAsync(TextMessage tm) =>
		await SendAsync(tm with { Message = tm.Message.Replace(":", "$C") });

	public async Task SendFlightplanAsync(FlightplanMessage fpl) =>
		await SendAsync(fpl);

	public async Task SendInformationReplyAsync(InformationReplyMessage ipm) =>
		await SendAsync(ipm);

	public async Task<AdministrativeMessage> RequestHandoffAsync(HandoffRequestMessage hrm, CancellationToken token)
	{
		_pendingHandoffs.Add(hrm.Pilot, null);
		await SendAsync(hrm);

		while (_pendingHandoffs[hrm.Pilot] is null)
			await Task.Delay(500, token);

		_pendingHandoffs.Remove(hrm.Pilot, out var retval);
		return retval!;
	}

	private async Task SendAsync(INetworkMessage inm) =>
		await _controller.SendLineAsync(inm.ToString());
}
