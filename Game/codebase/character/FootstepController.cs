using Godot;

namespace Vantix.Character;

/// <summary>
/// Deterministic distance-based footstep cadence. Pure logic (no Node3D/Physics/Random), so it is
/// server- and client-replay safe like <see cref="MovementController"/>.
///
/// State is a continuous <see cref="ContinuousPhase"/> in step units that grows by 1.0 per traveled
/// <see cref="SvConVars.FootstepStrideLength"/>; each integer crossing is a step. The same phase is the
/// master clock for the view-bob in LocalAnimation, so bob and step sound stay in sync.
///
/// The server runs this per player and broadcasts step events; remote clients play them spatially.
/// <see cref="StepLoudness"/> (0..1) is the gameplay-relevant audibility.
/// </summary>
public class FootstepController
{
	/// <summary>Tuning reference. Default is the global <see cref="ConVars.Sv"/>. Swappable for tests.</summary>
	public SvConVars Sv = ConVars.Sv;

	private double _phase;
	private bool _leftFoot;
	private bool _wasMoving;

	/// <summary>True in exactly the tick in which an audible step lands.</summary>
	public bool DidStepThisFrame { get; private set; }
	/// <summary>0..1 audibility of the step, speed-scaled. Shift causes the step to not be emitted at all.</summary>
	public float StepLoudness { get; private set; }
	/// <summary>Alternates per step (L/R) for deterministic client-side sample selection.</summary>
	public bool StepIsLeftFoot { get; private set; }

	/// <summary>Continuous step phase wrapped to [0,2), master clock for the view-bob. +1.0 per step,
	/// integers mark footplants; a full L+R gait cycle is 2.0. The wrap keeps float precision bounded.</summary>
	public float ContinuousPhase => (float)(_phase % 2.0);
	/// <summary>0..1 progress to the next step, e.g. for the debug overlay.</summary>
	public float StridePhase => (float)(_phase - Mathf.Floor((float)_phase));

	/// <summary>Resets the cadence. Caller: respawn / teleport, to avoid a phantom step afterwards.</summary>
	public void Reset()
	{
		_phase = 0.0;
		_leftFoot = false;
		_wasMoving = false;
		DidStepThisFrame = false;
	}

	/// <summary>Server-replayable footstep step. Advances the phase and emits step events.</summary>
	public void Step(FootstepInput input)
	{
		DidStepThisFrame = false;

		if (!input.OnFloor || input.IsSliding || input.HorizontalSpeed < Sv.FootstepMinSpeed)
		{
			_wasMoving = false;
			return;
		}

		float stride = StrideLength(input);

		if (!_wasMoving)
		{
			_wasMoving = true;
			_phase = Mathf.Floor((float)_phase) + Mathf.Clamp(Sv.FootstepInitialStepFraction, 0f, 0.99f);
		}

		int before = (int)System.Math.Floor(_phase);
		_phase += input.HorizontalSpeed * input.Dt / Mathf.Max(0.3f, stride);
		int after = (int)System.Math.Floor(_phase);
		if (after <= before) return;

		if (after - before > 1) _phase = before + 1.5;

		_leftFoot = !_leftFoot;
		StepIsLeftFoot = _leftFoot;
		StepLoudness = ComputeLoudness(input);
		DidStepThisFrame = StepLoudness > 0.001f;
	}

	/// <summary>Stride length (m) between two footsteps. Sprint shortens, crouch lengthens cadence.</summary>
	private float StrideLength(FootstepInput input)
	{
		float stride = Sv.FootstepStrideLength;
		if (input.IsSprinting) stride *= Sv.FootstepSprintStrideMul;
		else if (input.CrouchHeld) stride *= Sv.FootstepCrouchStrideMul;
		return Mathf.Max(0.3f, stride);
	}

	/// <summary>0..1 audibility. Shift returns 0 (silent); otherwise speed-banded and dampened by crouch.</summary>
	private float ComputeLoudness(FootstepInput input)
	{
		if (input.ShiftHeld) return 0f;

		float speed = input.HorizontalSpeed;
		float loud;
		if (speed <= Sv.WalkSpeed)
			loud = Mathf.Lerp(Sv.FootstepMinLoudness, Sv.FootstepWalkLoudness,
				Mathf.Clamp(speed / Mathf.Max(0.01f, Sv.WalkSpeed), 0f, 1f));
		else
			loud = Mathf.Lerp(Sv.FootstepWalkLoudness, Sv.FootstepSprintLoudness,
				Mathf.Clamp((speed - Sv.WalkSpeed) / Mathf.Max(0.01f, Sv.SprintSpeed - Sv.WalkSpeed), 0f, 1f));

		if (input.CrouchHeld) loud *= Sv.FootstepCrouchLoudnessMul;
		return Mathf.Clamp(loud, 0f, 1f);
	}
}
