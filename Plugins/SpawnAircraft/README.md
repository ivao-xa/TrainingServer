# Scenario Loader

Spawns a set of aircraft defined in a scenario file with a callsign at a given GPS coordinate, optionally with a starting true course, ground speed, and altitude MSL.  

The command must be sent on the server frequency (123.450 MHz) and be of the following form:
```
LOAD <file_name>
```
Note, the scenario file has to be loaded onto the server.

## Scenario File Structure
The scenario file can, at the moment, include **two** commands:
```
SPAWN - spawns an aircraft with the given parameters.
DELAY - delays the next spawn for a given time
```

If you want to know how to spawn an aircraft, you can check the [official repository](https://github.com/ivao-xa/TrainingServer/tree/main/Plugins/SpawnAircraft/README.md)


The ordering is important. The following commands are **invalid**:
```
N862SL AT 33.80741457 -118.34437255 ALT 050 SPD 100
N862SL AT 33.80741457 -118.34437255 SPD 100 HDG 180
```

Unless otherwise specified, aircraft will spawn at 100ft MSL flying true course 180 degrees at 100 kts ground speed.