using Godot;

[Tool, GlobalClass]
public partial class FpsDemoInput : Node
{
	[Export] public NodePath AnimatedCharacterPath;
	[Export(PropertyHint.Range, "0.01,2,0.01")] public float MouseSensitivity = 0.3f;
	[Export(PropertyHint.Range, "0.1,2,0.05")] public float MagCheckHoldThreshold = 0.4f;
	[Export(PropertyHint.Range, "0.1,2,0.05")] public float InspectEmptyHoldThreshold = 0.4f;
	[Export(PropertyHint.Range, "60,1500,10")] public float FireRatePerMinute = 600f;
	[Export(PropertyHint.Range, "1,100,1")] public int MagSize = 30;
	[Export] public bool CaptureMouseOnReady = true;

	private AnimatedCharacter _char;
	private int _fireModeIdx;
	private static readonly AnimatedCharacter.FireMode[] FireModes = { AnimatedCharacter.FireMode.Semi, AnimatedCharacter.FireMode.Auto };
	private bool _isAimingRMB;
	private bool _isCrouched;
	private bool _cantedAim;
	private bool _cameraShakeOn = true;

	private bool _rHeld, _rConsumed;
	private float _rHoldTime;
	private bool _tHeld, _tConsumed;
	private float _tHoldTime;
	private float _timeSinceFire = 999f;
	private bool _fireWasHeld;
	private int _ammo;

	private bool _vPrev, _fPrev, _gHeld, _qPrev, _ePrev, _hPrev;
	private bool _jPrev, _bPrev, _uPrev, _xPrev, _spacePrev, _cPrev, _lPrev, _escapePrev, _f7Prev;

	public int Ammo => _ammo;
	public int MagSizeValue => MagSize;
	public string FireModeName => FireModes[_fireModeIdx] == AnimatedCharacter.FireMode.Auto ? "Full Auto" : "Semi";

	private void UpdateMagFill() => _char?.SetMagazineFill((float)_ammo / Mathf.Max(1, MagSize));

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_char = GetNodeOrNull<AnimatedCharacter>(AnimatedCharacterPath);
		if (_char == null)
		{
			GD.PushWarning("[FpsDemoInput] AnimatedCharacterPath unresolved");
			return;
		}
		_ammo = MagSize;
		UpdateMagFill();
		_char.MouseLookEnabled = true;
		if (CaptureMouseOnReady) Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || _char == null) return;
		float dt = (float)delta;

		Vector2 move = Vector2.Zero;
		if (Input.IsKeyPressed(Key.W)) move.Y -= 1f;
		if (Input.IsKeyPressed(Key.S)) move.Y += 1f;
		if (Input.IsKeyPressed(Key.A)) move.X -= 1f;
		if (Input.IsKeyPressed(Key.D)) move.X += 1f;
		_char.SetMoveInput(move);

		if (Input.IsKeyPressed(Key.Alt)) _char.SetSpeedMode(AnimatedCharacter.SpeedMode.Sprint);
		else if (Input.IsKeyPressed(Key.Shift)) _char.SetSpeedMode(AnimatedCharacter.SpeedMode.Run);
		else _char.SetSpeedMode(AnimatedCharacter.SpeedMode.Walk);

		_timeSinceFire += dt;
		bool firePressed = Input.IsMouseButtonPressed(MouseButton.Left);
		float fireInterval = 60f / Mathf.Max(60f, FireRatePerMinute);
		AnimatedCharacter.FireMode mode = FireModes[_fireModeIdx];
		bool canRepeat = mode == AnimatedCharacter.FireMode.Auto || !_fireWasHeld;
		if (firePressed && _timeSinceFire >= fireInterval && canRepeat)
		{
			if (_ammo > 0)
			{
				_char.TriggerFire(mode, false);
				_ammo--;
				UpdateMagFill();
				_timeSinceFire = 0f;
			}
			else if (!_fireWasHeld)
			{
				_char.TriggerFire(mode, true);
				_timeSinceFire = 0f;
			}
		}
		_fireWasHeld = firePressed;

		bool rNow = Input.IsKeyPressed(Key.R);
		if (rNow && !_rHeld) { _rHeld = true; _rHoldTime = 0f; _rConsumed = false; }
		else if (!rNow && _rHeld) { _rHeld = false; if (!_rConsumed) { _char.TriggerReload(false, false); _ammo = MagSize; UpdateMagFill(); } }
		if (_rHeld) { _rHoldTime += dt; if (!_rConsumed && _rHoldTime >= MagCheckHoldThreshold) { _char.TriggerMagCheck(); _rConsumed = true; } }

		bool tNow = Input.IsKeyPressed(Key.T);
		if (tNow && !_tHeld) { _tHeld = true; _tHoldTime = 0f; _tConsumed = false; }
		else if (!tNow && _tHeld) { _tHeld = false; if (!_tConsumed) _char.TriggerInspect(false); }
		if (_tHeld) { _tHoldTime += dt; if (!_tConsumed && _tHoldTime >= InspectEmptyHoldThreshold) { _char.TriggerInspect(true); _tConsumed = true; } }

		JustPressed(Key.V, ref _vPrev, () => { _fireModeIdx = (_fireModeIdx + 1) % FireModes.Length; _char.TriggerFireModeSwitch(FireModes[_fireModeIdx]); });
		JustPressed(Key.F, ref _fPrev, () => _char.TriggerMelee(AnimatedCharacter.MeleeDirection.Bash));
		bool gNow = Input.IsKeyPressed(Key.G);
		if (gNow && !_gHeld) { _gHeld = true; _char.SetGrenadeAiming(true); }
		else if (!gNow && _gHeld) { _gHeld = false; _char.SetGrenadeAiming(false); _char.TriggerGrenadeThrow(); }
		JustPressed(Key.Q, ref _qPrev, () => { _char.TriggerReload(false, true); _ammo = MagSize; UpdateMagFill(); });
		JustPressed(Key.E, ref _ePrev, () => { _char.TriggerReload(true, false); _ammo = MagSize; UpdateMagFill(); });
		JustPressed(Key.H, ref _hPrev, () => _char.Equipping(false));
		JustPressed(Key.J, ref _jPrev, () => _char.TriggerClearJam(false));
		JustPressed(Key.B, ref _bPrev, () => _char.TriggerGripChange());
		JustPressed(Key.U, ref _uPrev, () => _char.TriggerHealSyringe());
		JustPressed(Key.X, ref _xPrev, () => _char.TriggerInteract(AnimatedCharacter.InteractKind.Grab));
		JustPressed(Key.Space, ref _spacePrev, () => _char.JumpStarted());
		JustPressed(Key.C, ref _cPrev, () => { _isCrouched = !_isCrouched; _char.SetCrouched(_isCrouched); });
		JustPressed(Key.F7, ref _f7Prev, () => _char.ToggleViewMode());
		JustPressed(Key.Escape, ref _escapePrev, () => GetTree().Quit());
	}

	private static void JustPressed(Key key, ref bool prev, System.Action action)
	{
		bool now = Input.IsKeyPressed(key);
		if (now && !prev) action();
		prev = now;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Engine.IsEditorHint() || _char == null) return;

		if (e is InputEventMouseMotion mm)
		{
			_char.SetLookDelta(mm.Relative * MouseSensitivity);
			return;
		}

		if (e is InputEventMouseButton mb)
		{
			switch (mb.ButtonIndex)
			{
				case MouseButton.Right:
					_isAimingRMB = mb.Pressed;
					_char.SetAiming(_isAimingRMB);
					break;
				case MouseButton.Middle:
					if (mb.Pressed) { _cantedAim = !_cantedAim; _char.SetCanted(_cantedAim); }
					break;
				case MouseButton.WheelUp:
					if (mb.Pressed) _char.AdjustTpsZoom(1f);
					break;
				case MouseButton.WheelDown:
					if (mb.Pressed) _char.AdjustTpsZoom(-1f);
					break;
			}
		}
	}
}
