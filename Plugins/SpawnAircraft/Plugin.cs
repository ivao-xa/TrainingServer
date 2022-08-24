﻿using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

namespace SpawnAircraft;

public class Plugin : IServerPlugin
{
#if DEBUG
	public string FriendlyName => "Aircraft Spawner (DEBUG)";
#else
    public string FriendlyName => "Aircraft Spawner";
#endif
    public string Maintainer => "Wes (644899)";

    private readonly Regex _spawnHeader,
                           _heading,
                           _speed,
                           _altitude,
                           _route;

    public Plugin()
    {
        string[] regexes = new[]
        {
            @"(?<callsign>\w+)\s+AT\s*(?<lat>[+-]?\d+(\.\d+)?)[ /;](?<lon>[+-]?\d+(\.\d+)?);?",
            @"HDG\s*(?<heading>\d+(.\d+)?)",
            @"SPD\s*(?<speed>\d+)",
            @"ALT\s*(?<altitude>-?\d+)",
            @"RTE(?<route>(\s+[+-]?\d+(\.\d+)?/[+-]?\d+(\.\d+)?)+)"
        };

        if (File.Exists("spawner.re") && File.ReadAllLines("spawner.re").Length >= 5)
            regexes = File.ReadAllLines("spawner.re").Select(l => l.Trim()).ToArray();
        else
            File.WriteAllLines("spawner.re", regexes);

        var rxo = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
        _spawnHeader = new("^" + regexes[0], rxo);
        _heading = new($"^{regexes[1]}", rxo);
        _speed = new($"^{regexes[2]}", rxo);
        _altitude = new($"^{regexes[3]}", rxo);
        _route = new($"^{regexes[4]}", rxo);
    }

    public bool CheckIntercept(string _, string message) => TryParse(message, out var _1, out var _2, out var _3, out var _4, out var _5, out var _6);

    private bool TryParse(string message, [NotNullWhen(true)] out string? callsign, [NotNullWhen(true)] out (double Lat, double Lon)? position, out float? heading, out uint? speed, out int? altitude, out string[]? route)
    {
        callsign = null;
        position = null;
        heading = null;
        speed = null;
        altitude = null;
        route = null;

        var match = _spawnHeader.Match(message);
        if (!match.Success)
            return false;

        message = message[match.Length..].TrimStart();

        callsign = match.Groups["callsign"].Value;
        position = (double.Parse(match.Groups["lat"].Value), double.Parse(match.Groups["lon"].Value));

        while (_altitude.IsMatch(message) || _heading.IsMatch(message) || _speed.IsMatch(message) || _route.IsMatch(message))
        {
            match = _altitude.Match(message);
            if (match.Success)
            {
                altitude = int.Parse(match.Groups["altitude"].Value) * 100;
                message = message[match.Length..].TrimStart();
                continue;
            }

            match = _heading.Match(message);
            if (match.Success)
            {
                heading = float.Parse(match.Groups["heading"].Value);
                message = message[match.Length..].TrimStart();
                continue;
            }

            match = _speed.Match(message);
            if (match.Success)
            {
                speed = uint.Parse(match.Groups["speed"].Value);
                message = message[match.Length..].TrimStart();
                continue;
            }

            match = _route.Match(message);
            if (match.Success)
            {
                route = match.Groups["route"].Value.Split(Array.Empty<char>(), StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                message = message[match.Length..].TrimStart();
                continue;
            }
        }

        return string.IsNullOrWhiteSpace(message);
    }

    public string? MessageReceived(IServer server, string sender, string message)
    {
        if (!TryParse(message, out string? callsign, out (double Lat, double Lon)? position, out float? heading, out uint? speed, out int? altitude, out string[]? route))
            throw new ArgumentException("Message was not a valid command", nameof(message));

        heading ??= 180f; speed ??= 100; altitude ??= 100;

        if (
            server.SpawnAircraft(
                callsign,
                new('?', '?', "1/UNKN/?-?/?", "N????", "????", new(), new(), "F???", "????", 0, 0, 0, 0, "????", "RMK/PLUGIN GENERATED AIRCRAFT. FLIGHT PLAN MAY BE INACCURATE.", string.Join(' ', route ?? new[] { "DCT" })),
                new() { Latitude = position.Value.Lat, Longitude = position.Value.Lon },
                heading.Value, speed.Value, altitude.Value
            ) is IAircraft ac
        )
        {
            if (route is not null)
                foreach (string wp in route)
                {
                    string[] elems = wp.Split('/');
                    ac.FlyDirect(new() { Latitude = double.Parse(elems[0]), Longitude = double.Parse(elems[1]) });
                }

            return $"Spawned aircraft {callsign}.";
        }
        else
            return $"Aicraft with callsign {callsign} already exists. Spawning failed.";
    }
}