# Simple Aircraft Spawner

Spawns an aircraft with a callsign at a given GPS coordinate, optionally with a starting true course, ground speed, altitude MSL, and route.  

The command must be sent on the server frequency (123.450 MHz) and be of the following form:
```
<callsign> AT <lat> <lon> HDG <heading> SPD <speed> ALT <alt> RTE <lat1>/<lon1> <lat2>/<lon2> …
```
where `HDG`, `SPD`, `ALT`, and `RTE` are optional.  

For example, consider the following commands. All are valid:
```
N862SL AT 33.80741457;-118.34437255;
N862SL AT 33.80741457 -118.34437255
N862SL AT 33.80741457/-118.34437255
N862SL AT 33.80741457;-118.34437255; ALT 050
N862SL AT 33.80741457 -118.34437255 HDG 180 ALT 050
N862SL AT 33.80741457 -118.34437255 SPD 100 ALT 050
N862SL AT 33.80741457 -118.34437255 SPD 100
N862SL AT 33.80741457 -118.34437255 ALT 050 SPD 100
N862SL AT 33.80741457 -118.34437255 SPD 100 HDG 180
N862SL AT 45.19109287;13.13151739; HDG 020 RTE 45.47467683/13.61107610 45.56754165/14.17143116 46.08545894/14.78028757
```

Unless otherwise specified, aircraft will spawn at 100ft MSL flying true course 180 degrees at 100 kts ground speed.

Wes (644899) & Niko (639233)