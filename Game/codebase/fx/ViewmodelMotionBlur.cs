using Godot;

namespace Vantix.Fx;

/// <summary>
/// Attaches a dedicated Compositor + <see cref="PostProcessEffect"/> to the weapon viewmodel's own
/// WorldEnvironment, since the weapon's own_world_3d SubViewport is invisible to the world Compositor
/// and would otherwise get no post-processing. <see cref="Configure"/> mirrors the world effect's toggles
/// and uses the same compositor-path gating so it never double-applies with the FSR2 <see cref="PostCanvasFx"/> path.
/// <see cref="Attach"/> wires the Compositor once after player spawn; <see cref="Configure"/> runs from Settings.ApplyEffects.
/// </summary>
public static class ViewmodelMotionBlur
{
	/// <summary>Weapon motion blur is deliberately weaker than the world's; high muzzle angular velocity would otherwise smear the gun front.</summary>
	private const float WeaponBlurStrength = 1.2f;

	private static PostProcessEffect _effect;

	/// <summary>The per-viewmodel PostProcessEffect (null before <see cref="Attach"/>); exposed so the ADS feed can push AdsBlend onto the weapon pass.</summary>
	public static PostProcessEffect Effect => _effect;

	/// <summary>Attaches a Compositor + PostProcessEffect to the viewmodel WorldEnvironment. Calling twice replaces the previous attachment; <see cref="Configure"/> sets real toggles after.</summary>
	public static void Attach(Node localPlayer)
	{
		if (localPlayer == null) return;
		WorldEnvironment vmEnv = FindViewmodelEnvironment(localPlayer);
		if (vmEnv == null)
		{
			Dbg.Print("[ViewmodelMotionBlur] viewmodel WorldEnvironment not found — skipping");
			return;
		}
		var comp = new Compositor();
		_effect = new PostProcessEffect { MotionBlur = true, Enabled = true, MotionBlurStrength = WeaponBlurStrength };
		comp.CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _effect };
		vmEnv.Compositor = comp;
		Dbg.Print("[ViewmodelMotionBlur] attached PostProcessEffect to viewmodel_env");
	}

	/// <summary>Mirrors the world effect's toggles onto the viewmodel effect (no-op if not attached). <paramref name="enabled"/> must follow the world effect's compositor-path gating so it never stacks on the FSR2 PostCanvasFx pass.</summary>
	public static void Configure(bool enabled, bool chromaticAberration, bool sharpening, bool vignette, bool filmGrain, bool motionBlur)
	{
		if (_effect == null) return;
		_effect.Enabled = enabled;
		_effect.ChromaticAberration = chromaticAberration;
		_effect.Sharpening = sharpening;
		_effect.Vignette = vignette;
		_effect.FilmGrain = filmGrain;
		_effect.MotionBlur = motionBlur;
	}

	/// <summary>Toggles the effect on/off. Safe to call before <see cref="Attach"/> — no-op if not yet attached.</summary>
	public static void SetEnabled(bool enabled)
	{
		if (_effect == null) return;
		_effect.Enabled = enabled;
	}

	/// <summary>Clears the stored reference so the next Attach starts fresh after a level/player reload.</summary>
	public static void Reset()
	{
		_effect = null;
	}

	/// <summary>True if the node lives inside an own_world_3d SubViewport (the viewmodel world); lets world-env finders skip the viewmodel's own compositor-bearing Environment.</summary>
	public static bool IsViewmodelEnvironment(Node node)
	{
		for (Node n = node; n != null; n = n.GetParent())
			if (n is SubViewport sv && sv.OwnWorld3D) return true;
		return false;
	}

	/// <summary>Returns the WorldEnvironment child of the LocalPlayer's own_world_3d SubViewport (the viewmodel viewport).</summary>
	private static WorldEnvironment FindViewmodelEnvironment(Node root)
	{
		foreach (Node n in root.FindChildren("*", "SubViewport", true, false))
		{
			if (n is not SubViewport sv || !sv.OwnWorld3D) continue;
			foreach (Node child in sv.GetChildren())
			{
				if (child is WorldEnvironment we) return we;
			}
		}
		return null;
	}
}
