using TrainingServer;
using TrainingServer.Extensibility;

using CIFPReader;

using CifpCoord = CIFPReader.Coordinate;
using TrainingCoord = TrainingServer.Coordinate;

namespace XAControl;

public class Plugin : IPlugin
{
	public string FriendlyName => "FAA-inspired aircraft control commands";

	public string Maintainer => "Wes (644899)";

    private readonly CIFP _cifp = CIFP.Load();

    readonly private Dictionary<string, Command> _commands = new()
	{
		{ "CI", Command.Procedure }, { "CR", Command.Procedure }, { "CP", Command.Procedure }, { "FP", Command.Procedure },
        { "PD", Command.Direct }, { "DCT", Command.Direct }, { "LD", Command.Direct }, { "RD", Command.Direct },
		{ "FH", Command.Heading }, { "HDG", Command.Heading }, { "L", Command.Heading }, { "R", Command.Heading },
		{ "DM", Command.Altitude }, { "D", Command.Altitude },
		{ "CM", Command.Altitude }, { "C", Command.Altitude },
		{ "SQ", Command.Squawk }, { "SQK", Command.Squawk },
		{ "S", Command.Speed }, { "IS", Command.Speed }, { "RS", Command.Speed }, { "SPD", Command.Speed },
		{ "CON", Command.Continue }, { "CONTINUE", Command.Continue },
		{ "RON", Command.ResumeOwnNavigation }, { "OWN", Command.ResumeOwnNavigation },
		{ "DIE", Command.Die }, { "END", Command.Die }
	};

	public Plugin() { }

	public bool CheckIntercept(string aircraftCallsign, string sender, string message) =>
		_commands.ContainsKey(message.TrimStart().Split()[0].ToUpperInvariant());

	public string? MessageReceived(IAircraft aircraft, string sender, string message)
	{
		string[] parts = message.Trim().ToUpperInvariant().Split();
		Command command = _commands[parts[0]];
		CifpCoord acPos = new((decimal)aircraft.Position.Latitude, (decimal)aircraft.Position.Longitude);

		void queueProcInstr(Procedure.Instruction instr)
		{
            switch (instr)
            {
                case Procedure.Instruction(_, ICoordinate ep, _, var spd, var alt, _):
                    aircraft.FlyDirect(new() { Latitude = (double)ep.Latitude, Longitude = (double)ep.Longitude });

					if (spd != SpeedRestriction.Unrestricted)
						aircraft.RestrictSpeed(spd.Minimum ?? spd.Maximum ?? 0, spd.Maximum ?? spd.Minimum ?? 1000, 30f);

					aircraft.PauseSpeedUntilWaypoint();

					if (alt != AltitudeRestriction.Unrestricted)
						aircraft.RestrictAltitude((alt.Minimum ?? alt.Maximum)?.ToMSL().Feet ?? 0, (alt.Maximum ?? alt.Minimum)?.ToMSL().Feet ?? 60000, 10);

					aircraft.PauseAltitudeUntilWaypoint();
                    break;

                case Procedure.Instruction(_, _, Course crs, var spd, var alt, _):
					aircraft.TurnCourse((float)crs.ToTrue().Degrees);

                    if (spd != SpeedRestriction.Unrestricted)
                        aircraft.RestrictSpeed(spd.Minimum ?? spd.Maximum ?? 0, spd.Maximum ?? spd.Minimum ?? 1000, 30f);

                    aircraft.PauseSpeedUntilWaypoint();

					if (alt != AltitudeRestriction.Unrestricted)
					{
						aircraft.RestrictAltitude((alt.Minimum ?? alt.Maximum)?.ToMSL().Feet ?? 0, (alt.Maximum ?? alt.Minimum)?.ToMSL().Feet ?? 60000, 10);
						aircraft.FlyAltitude();
					}
                    break;
            }
        }

        switch (command)
		{
			case Command.Procedure when parts.Length == 2 && _cifp.Procedures.TryGetValue(parts[1], out HashSet<Procedure>? procedures) && procedures is not null:
                aircraft.Interrupt();
				aircraft.InterruptSpeed();
				aircraft.InterruptVnav();
				Thread.Yield();
				var baseProc = procedures.OrderBy(p => _cifp.Aerodromes[p.Airport ?? "PHNL"].Location.GetCoordinate().DistanceTo(acPos)).First();
				foreach (var instr in baseProc.SelectRoute(null, null))
					queueProcInstr(instr);
                break;

            case Command.Procedure when parts.Length == 3 && _cifp.Procedures.TryGetValue(parts[1], out HashSet<Procedure>? procedures) && procedures is not null && procedures.Any(p => p.HasRoute(null, parts[2])):
                aircraft.Interrupt();
				aircraft.InterruptSpeed();
				aircraft.InterruptVnav();
				Thread.Yield();
				var transOutProc = procedures.Where(p => p.HasRoute(null, parts[2])).OrderBy(p => _cifp.Aerodromes[p.Airport ?? "PHNL"].Location.GetCoordinate().DistanceTo(acPos)).First();
                foreach (var instr in transOutProc.SelectRoute(null, parts[2]))
                    queueProcInstr(instr);
                break;

            case Command.Procedure when parts.Length == 3 && _cifp.Procedures.TryGetValue(parts[2], out HashSet<Procedure>? procedures) && procedures is not null && procedures.Any(p => p.HasRoute(parts[1], null)):
                aircraft.Interrupt();
				aircraft.InterruptSpeed();
				aircraft.InterruptVnav();
				Thread.Yield();
				var transInProc = procedures.Where(p => p.HasRoute(parts[1], null)).OrderBy(p => _cifp.Aerodromes[p.Airport ?? "PHNL"].Location.GetCoordinate().DistanceTo(acPos)).First();
                foreach (var instr in transInProc.SelectRoute(parts[1], null))
                    queueProcInstr(instr);
                break;

            case Command.Procedure when parts.Length == 4 && _cifp.Procedures.TryGetValue(parts[2], out HashSet<Procedure>? procedures) && procedures is not null && procedures.Any(p => p.HasRoute(parts[1], parts[3])):
                aircraft.Interrupt();
				aircraft.InterruptSpeed();
				aircraft.InterruptVnav();
				Thread.Yield();
				var transBothProc = procedures.Where(p => p.HasRoute(parts[1], parts[3])).OrderBy(p => _cifp.Aerodromes[p.Airport ?? "PHNL"].Location.GetCoordinate().DistanceTo(acPos)).First();
                foreach (var instr in transBothProc.SelectRoute(parts[1], parts[3]))
                    queueProcInstr(instr);
                break;

            case Command.Direct when parts.Length > 1 && _cifp.Fixes.TryConcretize(parts[1], out NamedCoordinate? coord, acPos):
				aircraft.Interrupt();
				Thread.Yield();
				var (_, (lat, lon)) = coord.Value;
                aircraft.FlyDirect(new TrainingCoord { Latitude = (double)lat, Longitude = (double)lon }, turnDirection: parts[0] switch { "LD" => TurnDirection.Left, "RD" => TurnDirection.Right, _ => null });
				break;

			case Command.Heading when parts.Length > 1 && uint.TryParse(parts[1], out uint hdg):
				aircraft.Interrupt();
				Thread.Yield();
				decimal magVar = _cifp.Aerodromes.GetLocalMagneticVariation(acPos).Variation;
				float adjHdg = (hdg + 360f - (float)magVar) % 360;
				aircraft.TurnCourse(adjHdg, turnDirection: parts[0] switch { "L" => TurnDirection.Left, "R" => TurnDirection.Right, _ => null });
				break;

			case Command.Altitude when parts.Length > 1 && int.TryParse(parts[1], out int alt):
				aircraft.InterruptVnav();
				Thread.Yield();
                aircraft.RestrictAltitude(alt * 100, alt * 100, (uint)(aircraft.Altitude > alt ? 2000 : 1000));
				break;

			case Command.Squawk when parts.Length > 1 && parts[1].Length == 4 && parts[1].All(char.IsDigit):
				aircraft.Squawk = ushort.Parse(parts[1]);
				break;

			case Command.Speed when parts.Length > 1 && uint.TryParse(parts[1], out uint spd):
				aircraft.InterruptSpeed();
				Thread.Yield();
				aircraft.RestrictSpeed(spd, spd, aircraft.GroundSpeed > spd ? 2.5f : 5f);
				break;

			case Command.Continue:
				// Allow both "CON" and "CON 3"
				foreach (var _ in Enumerable.Range(0, parts.Length > 1 ? uint.TryParse(parts[1], out uint ct) ? (int)ct : 1 : 1))
				{
					aircraft.ContinueLnav();
					aircraft.ContinueVnav();
					aircraft.ContinueSpeed();
				}

				return "Continuing";

			case Command.ResumeOwnNavigation:
				return aircraft.ResumeOwnNavigation() ? "Own navigation" : "Not sure what you want us to resume…";

			case Command.Die:
				aircraft.Kill();
				return "Good day.";

			default:
				return "Unable to execute command";
		}

		return "Wilco";
	}

	private enum Command
	{
		Direct,
		Procedure,
		Heading,
		Altitude,
		Squawk,
		Speed,
		Continue,
		ResumeOwnNavigation,
		Die
	}
}