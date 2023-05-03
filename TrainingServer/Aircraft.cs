using CIFPReader;

using IVAN.FSD.Protocol;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TrainingServer;

[JsonConverter(typeof(AircraftJsonConverter))]
internal partial class Aircraft : IAircraft, IDisposable
{
	public event EventHandler? Killed;

	public string Callsign { get; init; }

	public Flightplan Flightplan { get; init; }

	public Coordinate Position =>
		new() { Latitude = (double)_position.Latitude, Longitude = (double)_position.Longitude };
	public float TrueCourse =>
		(float)_currentCourse.Degrees;

	public uint GroundSpeed => (uint)_groundSpeed;
	public int Altitude => (int)_altitude;

	public ushort Squawk
	{
		get => _squawk;
		set
		{
			if (!_squawkCodeRegex.IsMatch(value.ToString()))
				throw new ArgumentOutOfRangeException(nameof(value));

			_squawk = value;
		}
	}

	public bool Paused
	{
		get => _paused;
		set
		{
			lock (_pauseLock)
			{
				_paused = value;
			}
		}
	}

	public PilotPositionUpdateMessage PositionUpdateMessage => new(
		Callsign, PilotPositionUpdateMessage.SquawkType.ModeC, Squawk, 4,
		new(_position.Latitude, _position.Longitude), Altitude, (int)GroundSpeed, (0, 0, 0, false), 0
	);

	private ushort _squawk = 2000;
	private CIFPReader.Coordinate _position;
	private Course _currentCourse;
	private TrueCourse? _turnToCourse;
	private TurnDirection? _turnDirection;
	private float _turnRate = 3;
	private float _altitude;
	private float _groundSpeed;
	private bool _paused = false;
	private readonly object _pauseLock = new();

	private static readonly Regex _squawkCodeRegex = SquawkCodeRegex();
	private readonly CancellationTokenSource _cts = new();
	private readonly AutoResetEvent _triggerLnavInstruction = new(true);
	private readonly AutoResetEvent _triggerVnavInstruction = new(true);
	private ConcurrentQueue<(InstructionType Instruction, object Data)> _pendingLnavInstructions = new();
	private readonly ConcurrentQueue<(InstructionType Instruction, object Data)> _pendingVnavInstructions = new();

	public Aircraft(string callsign, Flightplan fpl, Coordinate startingPosition, float startingCourse, uint startingSpeed, int startingAltitude)
	{
		Callsign = callsign;
		Flightplan = fpl;
		_position = new((decimal)startingPosition.Latitude, (decimal)startingPosition.Longitude);
		_currentCourse = new TrueCourse((decimal)startingCourse);
		_groundSpeed = startingSpeed;
		_altitude = startingAltitude;

		Task.Run(async () => await TickAircraftAsync(TimeSpan.FromSeconds(0.1), _cts.Token));
		Task.Run(async () => await ProcessLnavInstructionsAsync(_cts.Token));
		Task.Run(async () => await ProcessVnavInstructionsAsync(_cts.Token));
	}

	#region Monitoring Tasks
	/// <summary>Updates the aircrafts course, altitude, speed, and position.</summary>
	private async Task TickAircraftAsync(TimeSpan tickingFrequency, CancellationToken token)
	{
		DateTime nextUpdate = DateTime.UtcNow;

		while (!token.IsCancellationRequested)
		{
			nextUpdate += tickingFrequency;

			if (!_paused)
			{
				lock (_pauseLock)
				{
					if (_turnToCourse is not null)
					{
						decimal remainingAngle = _turnToCourse.Angle(_currentCourse),
								delta = (decimal)(_turnRate * tickingFrequency.TotalSeconds);
						if (Math.Abs(remainingAngle) < Math.Abs(delta))
						{
							_currentCourse = _turnToCourse;
							_turnToCourse = null;
						}
						else if (_turnDirection is null)
							_currentCourse += remainingAngle > 0 ? -delta : delta;
						else if (_turnDirection is TurnDirection.Left)
							_currentCourse -= delta;
						else
							_currentCourse += delta;
					}

					_stepAltitude?.Invoke(tickingFrequency);
					_stepSpeed?.Invoke(tickingFrequency);
					_position = _position.FixRadialDistance(_currentCourse, (decimal)(GroundSpeed * tickingFrequency.TotalHours));
				}
			}

			if (nextUpdate < DateTime.UtcNow)
				nextUpdate = DateTime.UtcNow;
			else
			{
				try { await Task.Delay(nextUpdate - DateTime.UtcNow, token); }
				catch { return; }
			}
		}
	}

	/// <summary>Dequeues LNAV instructions from the waitlist and executes them.</summary>
	private async Task ProcessLnavInstructionsAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			if (!_pendingLnavInstructions.TryDequeue(out var instr))
			{
				try { await Task.Delay(50, token); }
				catch { return; }
				continue;
			}

			_triggerLnavInstruction.WaitOne();
			cancelWait = false;

			(InstructionType iType, object? args) = instr;

			(iType switch
			{
				InstructionType.TurnCourse => (Action<object>)ExecTurnCourse,
				InstructionType.FlyDirect => ExecFlyDirect,
				InstructionType.FlyArc => ExecFlyArc,
				InstructionType.FlyDistance => ExecFlyDistance,
				InstructionType.FlyTime => ExecFlyTime,
				InstructionType.FlyAltitude => ExecFlyAltitude,
				InstructionType.FlyForever => ExecFlyForever,
				_ => throw new NotImplementedException()
			}).Invoke(args);
		}
	}

	/// <summary>Dequeues VNAV instructions from the waitlist and executes them.</summary>
	private async Task ProcessVnavInstructionsAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			if (!_pendingVnavInstructions.TryDequeue(out var instr))
			{
				try { await Task.Delay(50, token); }
				catch { return; }
				continue;
			}

			_triggerVnavInstruction.WaitOne();
			cancelWait = false;

			(InstructionType iType, object? args) = instr;

			(iType switch
			{
				InstructionType.RestrictAltitude => (Action<object>)ExecRestrictAltitude,
				InstructionType.RestrictSpeed => ExecRestrictSpeed,
				_ => throw new NotImplementedException()
			}).Invoke(args);
		}
	}
	#endregion

	#region Execute Instructions
	private bool cancelWait = false;
	private async Task WaitWhileAsync(Func<bool> condition, Action? eachLoop = null)
	{
		while (!cancelWait && condition.Invoke())
		{
			eachLoop?.Invoke();
			await Task.Delay(50);
		}
		cancelWait = false;

		_triggerLnavInstruction.Set();
	}

	private void ExecTurnCourse(object args)
	{
		(float trueCourse, float turnRate, TurnDirection? turnDirection) = ((float, float, TurnDirection?))args;
		_turnToCourse = new((decimal)trueCourse);
		_turnRate = turnRate;
		_turnDirection = turnDirection;

		Task.Run(async () => await WaitWhileAsync(() => _turnToCourse is not null));
	}

	private void ExecFlyDirect(object args)
	{
		(Coordinate destination, float turnRate, TurnDirection? turnDirection) = ((Coordinate, float, TurnDirection?))args;

		CIFPReader.Coordinate target = new((decimal)destination.Latitude, (decimal)destination.Longitude);
		_turnRate = turnRate;
		_turnDirection = turnDirection;

		// Wait until within 60ft.
		Task.Run(async () => await WaitWhileAsync(() => _position.DistanceTo(target) > 0.01m, () => _turnToCourse = _position.GetBearingDistance(target).bearing));
	}

	private void ExecFlyArc(object args)
	{
		(Coordinate arcCenterpoint, float degreesOfArc) = ((Coordinate, float))args;

		Task.Run(async () => await WaitWhileAsync(() => false));
	}

	private void ExecFlyDistance(object args)
	{
		float distance = (float)args;

		CIFPReader.Coordinate target = _position.FixRadialDistance(_turnToCourse ?? _currentCourse, (decimal)distance);

		ExecFlyDirect((new Coordinate() { Latitude = (double)target.Latitude, Longitude = (double)target.Longitude }, 3f));
	}

	private void ExecFlyTime(object args)
	{
		TimeSpan duration = (TimeSpan)args;

		DateTime until = DateTime.UtcNow + duration;

		Task.Run(async () => await WaitWhileAsync(() => DateTime.UtcNow < until));
	}

	private void ExecFlyForever(object args) => Task.Run(async () => await WaitWhileAsync(() => true));

	private void ExecFlyAltitude(object args)
	{
		if (args is Tuple<int, int, uint> a)
			ExecRestrictAltitude(a);

		Task.Run(async () => await WaitWhileAsync(() => _stepAltitude is not null));
	}

	private Action<TimeSpan>? _stepAltitude;
	private void ExecRestrictAltitude(object args)
	{
		(int minimum, int maximum, uint climbRate) = ((int, int, uint))args;

		_stepAltitude = ts =>
		{
			float change = (float)(climbRate * ts.TotalMinutes);

			if (_altitude < minimum)
			{
				_altitude += change;

				if (_altitude > maximum)
					_altitude = minimum;
			}
			else if (_altitude > maximum)
			{
				_altitude -= change;

				if (_altitude < minimum)
					_altitude = maximum;
			}
			else
				_stepAltitude = null;
		};

		_triggerVnavInstruction.Set();
	}

	private Action<TimeSpan>? _stepSpeed;
	private void ExecRestrictSpeed(object args)
	{
		(uint minimum, uint maximum, float acceleration) = ((uint, uint, float))args;

		_stepSpeed = ts =>
		{
			float change = (float)(acceleration * ts.TotalSeconds);

			if (_groundSpeed < minimum)
			{
				_groundSpeed += change;

				if (_groundSpeed > maximum)
					_groundSpeed = minimum;
			}
			else if (_groundSpeed > maximum)
			{
				_groundSpeed -= change;

				if (_groundSpeed < minimum)
					_groundSpeed = maximum;
			}
			else
				_stepSpeed = null;
		};

		_triggerVnavInstruction.Set();
	}
	#endregion

	#region Queue Instructions
	public void TurnCourse(float trueCourse, float turnRate = 3, TurnDirection? turnDirection = null) =>
		_pendingLnavInstructions.Enqueue((InstructionType.TurnCourse, (trueCourse, turnRate, turnDirection)));

	public void FlyDirect(Coordinate destination, float turnRate = 3, TurnDirection? turnDirection = null) =>
		_pendingLnavInstructions.Enqueue((InstructionType.FlyDirect, (destination, turnRate, turnDirection)));

	public void FlyDistance(float distance) =>
		_pendingLnavInstructions.Enqueue((InstructionType.FlyDistance, distance));

	public void FlyTime(TimeSpan duration) =>
		_pendingLnavInstructions.Enqueue((InstructionType.FlyTime, duration));

	public void FlyArc(Coordinate arcCenterpoint, float degreesOfArc) =>
		_pendingLnavInstructions.Enqueue((InstructionType.FlyArc, (arcCenterpoint, degreesOfArc)));

	public void FlyForever() =>
		_pendingLnavInstructions.Enqueue((InstructionType.FlyForever, new()));

	public void FlyAltitude()
	{
		if (_stepAltitude is null && !_pendingLnavInstructions.Any(i => i.Instruction == InstructionType.RestrictAltitude))
			throw new Exception("Altitude restriction must be issued before a FlyAltitude instruction.");

		_pendingLnavInstructions.Enqueue((InstructionType.FlyAltitude, new()));
	}
	public void FlyAltitude(int minimum, int maximum, uint climbRate)
	{
		if (minimum > maximum)
			throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum altitude must not be greater than maximum altitude.");

		_pendingLnavInstructions.Enqueue((InstructionType.FlyAltitude, (minimum, maximum, climbRate)));
	}

	public void RestrictAltitude(int minimum, int maximum, uint climbRate)
	{
		if (minimum > maximum)
			throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum altitude must not be greater than maximum altitude.");

		_pendingVnavInstructions.Enqueue((InstructionType.RestrictAltitude, (minimum, maximum, climbRate)));
	}

	public void RestrictSpeed(uint minimum, uint maximum, float acceleration)
	{
		if (minimum > maximum)
			throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum speed must not be greater than maximum speed.");

		if (acceleration < 0)
			acceleration *= -1;

		_pendingVnavInstructions.Enqueue((InstructionType.RestrictSpeed, (minimum, maximum, acceleration)));
	}

	private enum InstructionType
	{
		TurnCourse,
		FlyDirect,
		FlyDistance,
		FlyTime,
		FlyArc,
		FlyForever,
		FlyAltitude,
		RestrictAltitude,
		RestrictSpeed
	}
	#endregion

	private ConcurrentQueue<(InstructionType Instruction, object Data)>? _ronCache = null;
	public void Interrupt()
	{
		_ronCache = new(_pendingLnavInstructions);
		_pendingLnavInstructions.Clear();
		Continue();
	}

	public void Continue() =>
		cancelWait = true;

	public bool ResumeOwnNavigation()
	{
		if (_ronCache is null)
			return false;

		_pendingLnavInstructions.Clear();
		_pendingLnavInstructions = _ronCache;
		_ronCache = null;
		return true;
	}

	public string ToJson() => JsonSerializer.Serialize(this);

	public void Kill()
	{
		Killed?.Invoke(this, new());
		Dispose();
	}

	public void SendTextMessage(IServer server, string recipient, string message) =>
		((Server)server).SendText(new(Callsign, recipient, message.Replace(":", "$C")));

	public void Dispose()
	{
		_pendingLnavInstructions.Clear();
		_cts.Cancel();
	}

	[GeneratedRegex("^[0-7]{4}$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
	private static partial Regex SquawkCodeRegex();

	public override string ToString() => $"{Callsign} @ {_position.DMS}";
}

internal class AircraftJsonConverter : JsonConverter<Aircraft>
{
	public override Aircraft? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException();

		string? callsign = null; Flightplan? fpl = null;
		Coordinate? position = null; float? trueCourse = null;
		uint? groundSpeed = null; int? altitude = null; ushort? squawk = null;
		bool paused = false;

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
				break;
			else if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException();

			switch (reader.GetString()?.ToUpperInvariant())
			{
				case null: throw new JsonException();

				case "CALLSIGN":
					reader.Read();
					callsign = reader.GetString() ?? throw new JsonException();
					continue;

				case "PLAN":
					reader.Read();
					fpl = JsonSerializer.Deserialize<Flightplan>(ref reader, options);
					continue;

				case "POSITION":
					reader.Read();
					position = JsonSerializer.Deserialize<Coordinate>(ref reader, options);
					continue;

				case "COURSE":
					reader.Read();
					trueCourse = reader.GetSingle();
					continue;

				case "SPEED":
					reader.Read();
					groundSpeed = reader.GetUInt32();
					continue;

				case "ALTITUDE":
					reader.Read();
					altitude = reader.GetInt32();
					continue;

				case "SQUAWK":
					reader.Read();
					squawk = reader.GetUInt16();
					continue;

				case "PAUSED":
					reader.Read();
					paused = reader.GetBoolean();
					continue;

				default: throw new JsonException();
			}
		}

		if (callsign is null || fpl is null || position is null || trueCourse is null || groundSpeed is null || altitude is null || squawk is null)
			throw new JsonException();

		return new Aircraft(callsign, fpl.Value, position.Value, trueCourse.Value, groundSpeed.Value, altitude.Value) { Squawk = squawk.Value, Paused = paused };
	}

	public override void Write(Utf8JsonWriter writer, Aircraft value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteString("callsign", value.Callsign);
		writer.WritePropertyName("plan");
		JsonSerializer.Serialize(writer, value.Flightplan, options);
		writer.WritePropertyName("position");
		JsonSerializer.Serialize(writer, value.Position, options);
		writer.WriteNumber("course", value.TrueCourse);
		writer.WriteNumber("speed", value.GroundSpeed);
		writer.WriteNumber("altitude", value.Altitude);
		writer.WriteNumber("squawk", value.Squawk);
		if (value.Paused)
			writer.WriteBoolean("paused", value.Paused);
		writer.WriteEndObject();
	}
}