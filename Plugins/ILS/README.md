# ILS (LOC only)

This plugin simulates using PID control (with a fictitious plant) the aircraft joining the localizer of the ILS.

Command and data must be in the following form:
```
ILS <RWY threshold lat> <RWY threshold long> <RWY course (precision is required, get from charts)> <Airport altitude> <Glide slope>
```

Example commands:
```
ILS 40.47350560 -3.53618004 322 2000 3.0
```

Alvaro (519820)

