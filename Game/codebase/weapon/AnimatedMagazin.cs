using Godot;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class AnimatedMagazin : RigidBody3D
{
	private static readonly HashSet<string> AnimProps = new()
	{
		nameof(MagazineDepletion),
	};

	[ExportGroup("Physics")]
	// In-socket magazines (main + reserve) ride a bone socket — they must NOT collide or fall.
	// The dropped magazine (spawned by WeaponAnimation.DropMagazine) sets this true so it collides.
	[Export] public bool CollisionEnabled = false;

	[ExportGroup("Animation Player")]
	[Export] public NodePath AnimationPlayerPath = new("MergedAnimationPlayer");

	[ExportGroup("Animations")]
	[Export] public StringName MagazineDepletion;

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (!AnimProps.Contains((string)property["name"])) return;
		var player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
		if (player == null) return;
		property["hint"] = (int)PropertyHint.Enum;
		property["hint_string"] = string.Join(",", player.GetAnimationList());
	}

	private AnimationPlayer _player;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (!CollisionEnabled)
		{
			// Attached to the weapon's magazine socket: freeze so it rides the bone and never
			// participates in collisions (would otherwise fight the gun/hands).
			Freeze = true;
			FreezeMode = FreezeModeEnum.Static;
			CollisionLayer = 0;
			CollisionMask = 0;
			// The socket rotates every frame with the player; physics interpolation would lag the
			// body a frame behind its parent and make it flicker. Drive it straight from the node.
			PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		}
		_player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
		if (_player != null && !string.IsNullOrEmpty(MagazineDepletion?.ToString()) && _player.HasAnimation(MagazineDepletion))
			SanitizeRotationTracks(_player.GetAnimation(MagazineDepletion));
		SetFill(1f);
	}

	public void SetFill(float fill01)
	{
		if (_player == null || string.IsNullOrEmpty(MagazineDepletion?.ToString()) || !_player.HasAnimation(MagazineDepletion)) return;
		var anim = _player.GetAnimation(MagazineDepletion);
		double len = anim?.Length ?? 0.0;
		if (len <= 0.0) return;
		_player.Play(MagazineDepletion);
		_player.Seek(Mathf.Clamp(1f - fill01, 0f, 1f) * len, true);
		_player.Pause();
	}

	private static void SanitizeRotationTracks(Animation anim)
	{
		if (anim == null) return;
		for (int t = 0; t < anim.GetTrackCount(); t++)
		{
			if (anim.TrackGetType(t) != Animation.TrackType.Rotation3D) continue;
			int keys = anim.TrackGetKeyCount(t);
			for (int k = 0; k < keys; k++)
			{
				Quaternion q = (Quaternion)anim.TrackGetKeyValue(t, k);
				if (q.IsNormalized()) continue;
				anim.TrackSetKeyValue(t, k, q.LengthSquared() > 1e-6f ? q.Normalized() : Quaternion.Identity);
			}
		}
	}
}
