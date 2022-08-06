# Scenario Loader

Spawns a set of aircraft defined in a scenario file with a callsign at a given GPS coordinate, optionally with a starting true course, ground speed, and altitude MSL.  

The command must be sent on the server frequency (123.450 MHz) and be of the following form:
```
LOAD <file_name>
```
Note, the scenario file has to be loaded onto the server.

## Scenario File Structure
The scenario file can, at the moment, include the following commands:
```
SPAWN <aircraft> - spawns an aircraft with the given parameters.
DELAY <sec> - delays the next spawn for a given time.
DIEALL - deletes all instantiated aircraft.
<callsign> FH - flies heading
<callsign> C <alt> - climbs to specified altitude
<callsign> D <alt> - descends to specified altitude
<callsign> SPD <spd> - changes speed
<callsign> SQK (or SQ) - sets squawk for aircraft
<callsign> DIE - destroys specified aircraft
```

If you want to know how to spawn an aircraft, you can check the [official repository.](https://github.com/ivao-xa/TrainingServer/tree/main/Plugins/SpawnAircraft/README.md)

