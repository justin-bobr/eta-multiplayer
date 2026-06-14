using Godot;

namespace Vantix.Debug;

/// <summary>Free-look debug camera for flying through a scene at runtime (right mouse to look, WASD to move, mouse wheel to scale speed).</summary>
[GlobalClass]
public partial class FlyCamera : Camera3D
{
	[Export] public float MoveSpeed = 8.0f;
	[Export] public float BoostMultiplier = 4.0f;
	[Export] public float MouseSensitivity = 0.18f;
	[Export] public float WheelSpeedStep = 1.15f;
	[Export] public float MinSpeed = 0.25f;
	[Export] public float MaxSpeed = 400.0f;
	// Take over rendering as soon as the camera enters the tree (handy when dropped into a map for testing).
	[Export] public bool ActivateOnReady = true;

	private float _yaw;
	private float _pitch;
	private bool _looking;

	private Vector3 ReadMoveInput()
	{
		Vector3 dir = Vector3.Zero;
		if (Input.IsKeyPressed(Key.W)) dir -= GlobalTransform.Basis.Z;
		if (Input.IsKeyPressed(Key.S)) dir += GlobalTransform.Basis.Z;
		if (Input.IsKeyPressed(Key.A)) dir -= GlobalTransform.Basis.X;
		if (Input.IsKeyPressed(Key.D)) dir += GlobalTransform.Basis.X;
		if (Input.IsKeyPressed(Key.E) || Input.IsKeyPressed(Key.Space)) dir += Vector3.Up;
		if (Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.Ctrl)) dir -= Vector3.Up;
		return dir;
	}

	private void SetLooking(bool active)
	{
		_looking = active;
		Input.MouseMode = active ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
	}

	public override void _Ready()
	{
		Vector3 e = GlobalRotationDegrees;
		_pitch = e.X;
		_yaw = e.Y;
		if (ActivateOnReady) Current = true;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton button)
		{
			if (button.ButtonIndex == MouseButton.Right) SetLooking(button.Pressed);
			else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
				MoveSpeed = Mathf.Clamp(MoveSpeed * WheelSpeedStep, MinSpeed, MaxSpeed);
			else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
				MoveSpeed = Mathf.Clamp(MoveSpeed / WheelSpeedStep, MinSpeed, MaxSpeed);
		}
		else if (@event is InputEventMouseMotion motion && _looking)
		{
			_yaw -= motion.Relative.X * MouseSensitivity;
			_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -89.0f, 89.0f);
			GlobalRotationDegrees = new Vector3(_pitch, _yaw, 0.0f);
		}
	}

	public override void _Process(double delta)
	{
		Vector3 dir = ReadMoveInput();
		if (dir.LengthSquared() <= 0.0001f) return;
		float speed = Input.IsKeyPressed(Key.Shift) ? MoveSpeed * BoostMultiplier : MoveSpeed;
		GlobalPosition += dir.Normalized() * speed * (float)delta;
	}
}
