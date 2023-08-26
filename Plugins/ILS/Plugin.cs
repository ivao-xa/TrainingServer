using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TrainingServer;
using TrainingServer.Extensibility;

namespace ILS;

public class Plugin : IPlugin
{
#if DEBUG
    public string FriendlyName => "ILS (DEBUG)";
#else
    public string FriendlyName => "ILS";
#endif
    public string Maintainer => "Alvaro (519820)";

    private readonly Regex _ils;

    public Plugin()
    {

        var regexes = new[] {
            @"ILS\s(?<lat>[+-]?\d+(\.\d+)?)[ \/;](?<lon>[+-]?\d+(\.\d+)?);?\s*(?<hdg>\d+);?\s*(?<aptalt>\d+);?\s*(?<slope>[\d.]+)",
        };

        _ils = new(regexes[0], RegexOptions.IgnoreCase);
    }

    public bool CheckIntercept(string aircraftCallsign, string sender, string message)
    {
        return _ils.IsMatch(message);
    }

    private const double DistStep = 0.001;
    private const double Kp = 2.5;
    private const double Ki = 0.1;
    private const double Kd = 0.05;

    private static double Controller(double pos, Runway runway)
    {
        var error = pos - runway.runway_course;

        runway.integralError += error * DistStep;
        var derivativeError = (error - runway.errorLast) / DistStep;
        var output = Kp * error + Ki * runway.integralError + Kd * derivativeError;
        runway.errorLast = error;

        if ((runway.turn_toggle) || (Turn(output + runway.aircraft.TrueCourse, runway.initHdg, runway.turnDir)))
        {
            runway.turn_toggle = true;
            return output + runway.aircraft.TrueCourse;
        }
        // Fix integral wind up
        runway.integralError = 10;
        return runway.aircraft.TrueCourse;
    
 
    }

    private class Runway
    {
        public IAircraft aircraft;
        public Coordinate RunwayThreshold;
        public float runway_course;
        public float initHdg;
        public char turnDir;
        public double integralError;
        public double errorLast;
        public bool turn_toggle;
        public float aptalt;
        public float slope;
    }

    private static Dictionary<string, bool> aircarftOnILS = new();

    private static double Distancenm(Coordinate point1, Coordinate point2)
    {
        const double R = 3443.92; // Earth radius in nm

        double phi1 = point1.Latitude * Math.PI / 180;
        double phi2 = point2.Latitude * Math.PI / 180;

       double Delta_phi = (point2.Latitude - point1.Latitude) * Math.PI / 180;
       double Delta_lambda = (point2.Longitude - point1.Longitude) * Math.PI / 180;

       var a = Math.Sin(Delta_phi / 2) * Math.Sin(Delta_phi / 2) + Math.Cos(phi1) * Math.Cos(phi2) *
           Math.Sin(Delta_lambda / 2) * Math.Sin(Delta_lambda / 2);
       var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

       return R * c;
    }

    private static double BrngFromVec(double lat1, double lon1, double lat2, double lon2)
    {
        var lat1Rad = lat1 * Math.PI / 180;
        var lat2Rad = lat2 * Math.PI / 180;
        var delta = (lon2 - lon1) * Math.PI / 180;

        var y = Math.Sin(delta) * Math.Cos(lat2Rad);
        var x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) - Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(delta);

        var brng = Math.Atan2(y, x) * 180 / Math.PI;

        return brng < 0 ? brng + 360 : brng;
    }

    private static bool Turn(double hdg, double initHdg, char turnDir)
    {
        if ((hdg > initHdg) && (turnDir == 'L'))
        {
            return false;
        }

        if ((hdg < initHdg) && (turnDir == 'R'))
        {
            return false;
        }

        return true;
    }

    public string? MessageReceived(IAircraft aircraft, string sender, string message)
    {
        var match = _ils.Match(message);

        // Perform initial position checks
        Runway ILS = new Runway();
        ILS.aircraft = aircraft;
        ILS.RunwayThreshold.Latitude = double.Parse(match.Groups["lat"].Value);
        ILS.RunwayThreshold.Longitude = double.Parse(match.Groups["lon"].Value);
        ILS.runway_course = float.Parse(match.Groups["hdg"].Value);
        ILS.initHdg = aircraft.TrueCourse;
        ILS.turn_toggle = false;
        ILS.aptalt = float.Parse(match.Groups["aptalt"].Value);
        ILS.slope = (float)(float.Parse(match.Groups["slope"].Value) * Math.PI / 180);

        var relative = BrngFromVec(ILS.aircraft.Position.Latitude, ILS.aircraft.Position.Longitude, ILS.RunwayThreshold.Latitude, ILS.RunwayThreshold.Longitude);
        var tempRwyHdg = ILS.runway_course;
        var tempAcftHdg = aircraft.TrueCourse;

        // 0 degree fix
        if (tempAcftHdg + 90 < tempRwyHdg)
        {
            tempAcftHdg += 360;
        }
        else if (tempRwyHdg + 90 < tempAcftHdg)
        {
            tempRwyHdg += 360;
            relative += 360;
        }

        if ((tempAcftHdg > relative) && (relative > tempRwyHdg))
        {
            ILS.turnDir = (char)'L';
        }
            
        else if ((tempAcftHdg < relative) && (relative < tempRwyHdg))
        {
            ILS.turnDir = (char)'R';
        }
        else
        {
            return "Already passed the loc";
        }


        aircarftOnILS.Add(aircraft.Callsign, true);
        var ILStask = Task.Run(() => IlsPid(ILS));

        return "Controller started";
    }


    static async Task<bool> IlsPid(Runway runway)
    {
        double old_distance = 100;
        bool gs_trigger = false;

        Dictionary<uint, float> spds= new Dictionary<uint, float>
        {
            {180, 12},
            {160, 9},
            {130, 4}
        };


        while (aircarftOnILS[runway.aircraft.Callsign])
        {
            // Loc
            var rwyOffset = BrngFromVec(runway.aircraft.Position.Latitude, runway.aircraft.Position.Longitude, runway.RunwayThreshold.Latitude, runway.RunwayThreshold.Longitude);
            double acftHdg = Controller(rwyOffset, runway);
            runway.aircraft.TurnCourse((float)acftHdg, 1000F);

            // Spd
            double current_distance = Distancenm(runway.aircraft.Position, runway.RunwayThreshold);
            foreach (KeyValuePair<uint, float> entry in spds)
            {
                if ((entry.Value > current_distance) && (entry.Value < old_distance) && (runway.aircraft.GroundSpeed > entry.Key))
                {
                    runway.aircraft.RestrictSpeed(entry.Key, entry.Key, (float)2.5);
                }
            }

            // GS
            
            // Check if we are near the loc
            if ((rwyOffset - runway.runway_course < 1) && !gs_trigger && (runway.aircraft.Altitude - 100) < (Math.Abs(runway.aptalt + Distancenm(runway.aircraft.Position,
                    runway.RunwayThreshold) * 6076 * Math.Tan(runway.slope))) && (Math.Abs(runway.aptalt + Distancenm(runway.aircraft.Position,
                    runway.RunwayThreshold) * 6076 * Math.Tan(runway.slope))) < (100 + runway.aircraft.Altitude))
            {
                gs_trigger = true;
            }

            // Actualy descend if on gs
            if (gs_trigger)
            {
                runway.aircraft.RestrictAltitude((int)runway.aptalt, (int)runway.aptalt, (uint)((uint)runway.aircraft.GroundSpeed * 101.3
                    * Math.Tan(runway.slope)));
            }

            // Kill aircraft if landed
            if ((runway.aircraft.Altitude < runway.aptalt + 100) && (Distancenm(runway.aircraft.Position, runway.RunwayThreshold) < 0.3))
            {
                runway.aircraft.Kill();
                aircarftOnILS.Remove(runway.aircraft.Callsign);
            }


            Thread.Sleep(1000);

            if (!aircarftOnILS[runway.aircraft.Callsign])
                break;
        }

        aircarftOnILS.Remove(runway.aircraft.Callsign);

        return true;
    }
}
