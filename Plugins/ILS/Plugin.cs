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

        string[] regexes = new[] {
            @"ILS\s(?<lat>[+-]?\d+(\.\d+)?)[ /;](?<lon>[+-]?\d+(\.\d+)?);?\s*(?<hdg>\d+(.\d+)?)",
        };

        _ils = new(regexes[0], RegexOptions.IgnoreCase);
    }

    public bool CheckIntercept(string aircraftCallsign, string sender, string message)
    {
        return _ils.IsMatch(message);
    }

    private double _integralError;
    private double _errorLast;
    private double _target;

    private const double DistStep = 0.001;
    private const double Kp = 2.5;
    private const double Ki = 0.16;
    private const double Kd = 0.05;


    public double Controller(double pos)
    {
        double error = pos - _target;

        this._integralError += error * DistStep;
        double derivativeError = (error - _errorLast) / DistStep;
        double output = Kp * error + Ki * _integralError + Kd * derivativeError;
        this._errorLast = error;

        return output;
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

    private static (double, double) Plant(double hdg1, double lat1, double lon1, double d)
    {
        // Convert all to radians
        lat1 = lat1 * Math.PI / 180;
        lon1 = lon1 * Math.PI / 180;
        hdg1 = hdg1 * Math.PI / 180;
        d = d * Math.PI / 180;

        // See https://edwilliams.org/avform147.htm#LL

        var lat = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(hdg1));
        var lon = lon1 + Math.Atan2(Math.Sin(hdg1) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat));

        lat = lat * 180 / Math.PI;
        lon = lon * 180 / Math.PI;

        return (lat, lon);
    }

    private static double Turn(double hdg, double initHdg, char turnDir)
    {
        if ((hdg > initHdg) && (turnDir == 'L'))
        {
            return initHdg;
        }

        if ((hdg < initHdg) && (turnDir == 'R'))
        {
            return initHdg;
        }

        return hdg;
    }

    public string MessageReceived(IAircraft aircraft, string sender, string message)
    {
        var first = true;
        char turnDir;
        var additional = 5;

        double acftHdg = aircraft.TrueCourse;
        var initHdg = acftHdg;

        var points = new List<(double, double)> { (aircraft.Position.Latitude, aircraft.Position.Longitude) };

        var match = _ils.Match(message);

        // Load and vectorize all data
        var rwyPoint = new double[2];
        rwyPoint[0] = double.Parse(match.Groups["lat"].Value);
        rwyPoint[1] = double.Parse(match.Groups["lon"].Value);

        _target = float.Parse(match.Groups["hdg"].Value);

        var relative = BrngFromVec(points[0].Item1, points[0].Item2, rwyPoint[0], rwyPoint[1]);
        var tempRwyHdg = _target;
        var tempAcftHdg = acftHdg;

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
            turnDir = 'L';
        else if ((tempAcftHdg < relative) && (relative < tempRwyHdg))
            turnDir = 'R';
        else
            return "Already passed the loc";

        // PID
        for (var i = 0; i < 1000; i++)
        {
            var rwyOffset = BrngFromVec(points[^1].Item1, points[^1].Item2, rwyPoint[0], rwyPoint[1]);

            acftHdg = Turn(Controller(rwyOffset) + acftHdg, initHdg, turnDir);

            var tempPoint = Plant(acftHdg, points[^1].Item1, points[^1].Item2, DistStep);

            points.Add(tempPoint);

            if (first)
            {
                first = false;
            }
            else
            {
                if (Math.Abs(BrngFromVec(points[^1].Item1, points[^1].Item2, rwyPoint[0], rwyPoint[1]) - _target) < 0.2)
                {
                    if (additional == 0)
                    {
                        break;
                    }

                    additional -= 1;
                }
                else if (Math.Abs(BrngFromVec(points[^3].Item1, points[^3].Item2, points[^2].Item1, points[^2].Item2) - BrngFromVec(points[^2].Item1, points[^2].Item2, points[^1].Item1, points[^1].Item2)) < 1)
                {
                    points.RemoveAt(points.Count - 2);
                }
            }

        }


        for (int i = 1; i < points.Count; i++) // i starts in 1 to avoid first point at acft position
            aircraft.FlyDirect(new()
            {
                Latitude = points[i].Item1,
                Longitude = points[i].Item2
            });

        aircraft.FlyDirect(new () {
                Latitude = rwyPoint[0], Longitude = rwyPoint[1]
        });
        
        return "Following LOC";
    }
}
