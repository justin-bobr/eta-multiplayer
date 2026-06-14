namespace Vantix.Character;

/// <summary>Per-tick locomotion state the footstep system reads to time and pick footstep sounds.</summary>
public struct FootstepInput
{
	public float Dt;
	public float HorizontalSpeed;
	public bool OnFloor;
	public bool ShiftHeld;
	public bool CrouchHeld;
	public bool IsSprinting;
	public bool IsSliding;
}
