# Simple Aircraft Spawner

Spawns an aircraft with a callsign at a given GPS coordinate, optionally with a starting true course, ground speed, and altitude MSL.  

The command must be sent on the server frequency (123.450 MHz) and be of the following form:
```
<callsign> AT <lat> <lon> HDG <heading> SPD <speed> ALT <alt>
```
where `HDG`, `SPD`, and `ALT` are optional.  

For example, consider the following commands. All are valid:
```
N862SL AT 33.80741457;-118.34437255;
N862SL AT 33.80741457 -118.34437255
N862SL AT 33.80741457/-118.34437255
N862SL AT 33.80741457;-118.34437255; ALT 050
N862SL AT 33.80741457 -118.34437255 HDG 180 ALT 050
N862SL AT 33.80741457 -118.34437255 HDG 180 SPD 100
N862SL AT 33.80741457 -118.34437255 SPD 100 ALT 050
N862SL AT 33.80741457 -118.34437255 SPD 100
```

The ordering is important. The following commands are **invalid**:
```
N862SL AT 33.80741457 -118.34437255 ALT 050 SPD 100
N862SL AT 33.80741457 -118.34437255 SPD 100 HDG 180
```

Unless otherwise specified, aircraft will spawn at 100ft MSL flying true course 180 degrees at 100 kts ground speed.