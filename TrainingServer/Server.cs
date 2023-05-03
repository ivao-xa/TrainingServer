using IVAN.FSD;
using IVAN.FSD.Protocol;

using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

using TrainingServer;
using TrainingServer.Extensibility;

internal class Server : IServer
{
    private static readonly TimeSpan UPDATE_FREQUENCY = TimeSpan.FromSeconds(1);

    private static void Main(string[] args)
    {
        Server _ = new();

        if (args.Any(a => string.Equals(a, "-b", StringComparison.InvariantCultureIgnoreCase)))
            Task.Delay(-1).Wait();
        else
		{
			Console.WriteLine("Press ENTER to quit.");
			Console.ReadLine();
        }
    }

    private readonly CancellationTokenSource _cts = new();

    private readonly Dictionary<string, Aircraft> _aircraft = new();
    private readonly HashSet<AtcManager> _controllers = new();

    private readonly List<IServerPlugin> _serverPlugins = new();
    private readonly List<IRewriter> _rewriters = new();
    private readonly List<IPlugin> _plugins = new();

    public Server()
    {
        AssemblyName asmName = Assembly.GetExecutingAssembly().GetName();
        Console.WriteLine($"IVAO training server v{asmName.Version?.Major.ToString() ?? "?"}.{asmName.Version?.Minor.ToString() ?? "?"}.{asmName.Version?.Build.ToString() ?? "?"} by Wes (XA)");

        LoadPlugins(Directory.EnumerateFiles(".", "*.dll", SearchOption.AllDirectories));

        Task.Run(() => _ = ListenAsync(_cts.Token));
        Task.Run(() => _ = HeartbeatAsync(_cts.Token));
    }

    private void LoadPlugins(IEnumerable<string> files)
    {
        Dictionary<Type, object?> instantiationCache = new();

        foreach (string fp in files)
        {
            try
            {
                Assembly asm = Assembly.LoadFrom(fp);

                foreach (Type t in asm.ExportedTypes.Where(t => t.GetInterfaces().Contains(typeof(IServerPlugin))))
                {
                    if (!instantiationCache.ContainsKey(t) || instantiationCache[t] is null)
                        instantiationCache[t] = t.GetConstructor(Array.Empty<Type>())?.Invoke(null);

                    IServerPlugin? p = (IServerPlugin?)instantiationCache[t];

                    if (p is null)
                    {
                        Console.Error.WriteLine($"Could not find parameterless constructor for {t.Name} from file {fp}");
                        continue;
                    }


                    if (p.GetType().GetMethod("CatchAll", new Type[] { typeof(string), typeof(string), typeof(string) }) is not null)
                        Console.WriteLine($"Loaded SERVER plugin \"{p.FriendlyName}\" WITH CATCH-ALL by {p.Maintainer}.");
                    else
                        Console.WriteLine($"Loaded SERVER plugin \"{p.FriendlyName}\" by {p.Maintainer}.");
                    _serverPlugins.Add(p);
                }

                foreach (Type t in asm.ExportedTypes.Where(t => t.GetInterfaces().Contains(typeof(IRewriter))))
				{
					if (!instantiationCache.ContainsKey(t) || instantiationCache[t] is null)
						instantiationCache[t] = t.GetConstructor(Array.Empty<Type>())?.Invoke(null);

					IRewriter? r = (IRewriter?)instantiationCache[t];

                    if (r is null)
                    {
                        Console.Error.WriteLine($"Could not find parameterless constructor for {t.Name} from file {fp}");
                        continue;
                    }

                    Console.WriteLine($"Loaded rewriter \"{r.FriendlyName}\" by {r.Maintainer}.");
                    _rewriters.Add(r);
                }

                foreach (Type t in asm.ExportedTypes.Where(t => t.GetInterfaces().Contains(typeof(IPlugin))))
				{
					if (!instantiationCache.ContainsKey(t) || instantiationCache[t] is null)
						instantiationCache[t] = t.GetConstructor(Array.Empty<Type>())?.Invoke(null);

					IPlugin? p = (IPlugin?)instantiationCache[t];

                    if (p is null)
                    {
                        Console.Error.WriteLine($"Could not find parameterless constructor for {t.Name} from file {fp}");
                        continue;
                    }

                    Console.WriteLine($"Loaded plugin \"{p.FriendlyName}\" by {p.Maintainer}.");
                    _plugins.Add(p);
                }
            }
            catch (FileLoadException ex)
            {
                // Assembly already loaded somehow?
                Console.Error.WriteLine($"Error loading {fp}: " + ex.StackTrace);
            }
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        TcpListener server = new(System.Net.IPAddress.Any, 6809);
        server.Start();

        while (!token.IsCancellationRequested)
        {
            Socket incomingSock;
            try { incomingSock = await server.AcceptSocketAsync(token); }
            catch { break; }
            Console.WriteLine($"Connection from {incomingSock.RemoteEndPoint}.");
            SocketLineStream sls = new(incomingSock);

            // Create the manager with callbacks to populate the ATC list.
            AtcManager atcMan = new(sls, this, async am => { foreach (var c in _controllers.Where(c => c.AtcInfo is not null)) { await c.DistributeCommunicationAsync(am.AtcInfo!); await am.DistributeCommunicationAsync(c.AtcInfo!); } });
            atcMan.TextMessageReceived += TextMessageReceived;
            atcMan.InfoRequestReceived += InfoRequestReceived;
            _controllers.Add(atcMan);
        }
    }

    private void InfoRequestReceived(object? sender, InformationRequestMessage irm)
    {
        AtcManager atm = (AtcManager)sender!;

        switch (irm.Command)
        {
            case AdministrativeMessage.InformationCommand.Name:
                _ = atm.SendInformationReplyAsync(new InformationReplyMessage(irm.Destination, irm.Source, AdministrativeMessage.InformationCommand.Name, new[] { "Server Bot" }));
                break;

            case AdministrativeMessage.InformationCommand.FlightPlan:
                Flightplan fpl =
                    _aircraft.TryGetValue(irm.Fields[0], out var ac)
                    ? ac.Flightplan
                    : new('?', '?', "1/UNKN/?-?/?", "N????", "????", new(), new(), "F???", "????", 0, 0, 0, 0, "????", "RMK/PLUGIN GENERATED AIRCRAFT. FLIGHT PLAN MAY BE INACCURATE.", "DCT");
                _ = atm.SendFlightplanAsync(new FlightplanMessage(irm.Fields[0], irm.Source, fpl.FlightRules, fpl.TypeOfFlight, fpl.AircraftType, fpl.CruiseSpeed, fpl.DepartureAirport, new(fpl.EstimatedDeparture.Hour, fpl.EstimatedDeparture.Minute), new(fpl.ActualDeparture.Hour, fpl.ActualDeparture.Minute), fpl.CruiseAlt, fpl.ArrivalAirport, fpl.HoursEnRoute, fpl.MinutesEnRoute, fpl.HoursFuel, fpl.MinutesFuel, fpl.AlternateAirport, fpl.Remarks, fpl.Route));
                break;

            default:
                Console.Error.WriteLine($"Unknown information request command {irm.Command} from {atm.Callsign}.");
                break;
        }
    }

    private async Task HeartbeatAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(UPDATE_FREQUENCY, token); }
            catch { break; }

            if (!_controllers.Any())
                continue;

            foreach (Aircraft ac in _aircraft.Values.ToArray())
            {
                PilotPositionUpdateMessage ppum = ac.PositionUpdateMessage;
                _controllers.AsParallel().Where(c => c.Callsign is not null).ForAll(async c => await c.DistributePositionAsync(ppum));
            }
        }
    }

    private TextMessage Rewrite(TextMessage tm)
    {
        string msg = tm.Message;
        while (_rewriters.Any(r => r.Pattern.IsMatch(msg)))
            msg = _rewriters.First(r => r.Pattern.IsMatch(msg)).Rewrite(msg);

        return tm with { Message = msg };
    }

    private void TextMessageReceived(object? sender, TextMessage tm)
    {
        Aircraft ac;

        if (tm.Destination == "@23450")
        {
            tm = Rewrite(tm);

            IServerPlugin[] sInterceptors = _serverPlugins.Where(p => p.CheckIntercept(tm.Source, tm.Message)).ToArray();
            if (!sInterceptors.Any())
                return;

            IServerPlugin sIntercepting = sInterceptors.First();
            if (sInterceptors.Length > 1)
                Console.Error.WriteLine($"Incompatable plugins ({string.Join(", ", sInterceptors.Select(i => i.FriendlyName))}) fighting for message. Given to {sIntercepting.FriendlyName}.");

            string? sResponse = sIntercepting.MessageReceived(this, tm.Source, tm.Message);

            if (sResponse is not null && sender is AtcManager sam)
                _ = sam.SendTextAsync(new("SERVER", "@23450", sResponse));

            return;
        }
        else if (tm.Destination.StartsWith("@") && tm.Message.Contains(',') && _aircraft.ContainsKey(tm.Message.Split(',')[0].Trim()))
        {
            string acCallsign = tm.Message.Split(',')[0].Trim();
            tm = tm with { Message = tm.Message[(tm.Message.IndexOf(',') + 1)..].TrimStart() };
            ac = _aircraft[acCallsign];
        }
        else if (_controllers.Any(c => c.Callsign == tm.Destination))
        {
            _ = _controllers.First(c => c.Callsign == tm.Destination).SendTextAsync(tm);
            return;
        }
        else if (_aircraft.ContainsKey(tm.Destination))
            ac = _aircraft[tm.Destination];
        else
        {
            // Check for special 'catch-all' plugins.
            foreach ((IServerPlugin p, MethodInfo? catcher) in _serverPlugins.Select(p => (p, p.GetType().GetMethod("CatchAll", new Type[] { typeof(string), typeof(string), typeof(string) }))))
            {
                if (catcher is null)
                    continue;

                catcher.Invoke(p, new string[] { tm.Destination, tm.Source, tm.Message });
            }
            return;
        }

        tm = Rewrite(tm);

        IPlugin[] interceptors = _plugins.Where(p => p.CheckIntercept(ac.Callsign, tm.Source, tm.Message)).ToArray();
        if (!interceptors.Any())
            return;

        IPlugin intercepting = interceptors.First();
        if (interceptors.Length > 1)
            Console.Error.WriteLine($"Incompatable plugins ({string.Join(", ", interceptors.Select(i => i.FriendlyName))}) fighting for message. Given to {intercepting.FriendlyName}.");

        string? response = intercepting.MessageReceived(ac, tm.Source, tm.Message);

        if (response is not null && sender is AtcManager am)
            _ = am.SendTextAsync(new(ac.Callsign, tm.Destination.StartsWith('@') ? tm.Destination : tm.Source, response));
    }

    public IAircraft? SpawnAircraft(string callsign, Flightplan flightplan, Coordinate startPosition, float startHeading, uint startSpeed, int startAltitude)
    {
        lock (_aircraft)
        {
            bool canSpawn = !_aircraft.ContainsKey(callsign);
            if (canSpawn)
            {
                Aircraft ac = new(callsign, flightplan, startPosition, startHeading, startSpeed, startAltitude);
                _aircraft.Add(callsign, ac);
                ac.Killed += (a, _) =>
                {
                    string callsign = ((Aircraft?)a)?.Callsign ?? throw new ArgumentNullException(nameof(a));
                    _aircraft.Remove(callsign);
                    _controllers.AsParallel().ForAll(async c => await c.DistributeDeletionAsync(new(callsign)));
                };
                return ac;
            }
        }

        return null;
    }

    public IAircraft? SpawnAircraft(string acJson)
    {
        Aircraft? ac = JsonSerializer.Deserialize<Aircraft>(acJson);
        if (ac is null || _aircraft.ContainsKey(ac.Callsign))
            return null;

        _aircraft.Add(ac.Callsign, ac);
        ac.Killed += (a, _) =>
        {
            string callsign = ((Aircraft?)a)?.Callsign ?? throw new ArgumentNullException(nameof(a));
            _aircraft.Remove(callsign);
            _controllers.AsParallel().ForAll(async c => await c.DistributeDeletionAsync(new(callsign)));
        };
        return ac;
    }

    public void SendText(TextMessage tm)
    {
        foreach (var c in _controllers.Where(c => c.Callsign?.Equals(tm.Destination, StringComparison.InvariantCultureIgnoreCase) ?? true))
            _ = c.SendTextAsync(tm);
    }

    public async Task<AdministrativeMessage> TransferAsync(HandoffRequestMessage handoffRequest, CancellationToken token)
    {
        AtcManager? target = _controllers.FirstOrDefault(c => c.Callsign?.Equals(handoffRequest.Destination, StringComparison.InvariantCultureIgnoreCase) ?? false);

        if (target is null)
            return new HandoffRejectMessage(handoffRequest.Destination, handoffRequest.Source, handoffRequest.Pilot);

        return await target.RequestHandoffAsync(handoffRequest, token);
    }
}