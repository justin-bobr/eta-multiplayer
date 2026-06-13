using Godot;

/// <summary>
/// Attaches a dedicated Compositor + <see cref="PostProcessEffect"/> to the weapon viewmodel's
/// own WorldEnvironment. The weapon lives in its own SubViewport (transparent_bg, own_world_3d)
/// which means the WORLD's Compositor never sees it — so without a dedicated effect the weapon
/// gets NO screen-space post-processing at all (no CA / sharpen / vignette / grain / motion blur),
/// and looks visibly un-integrated vs the post-processed world.
///
/// <see cref="Configure"/> mirrors the world PostProcessEffect's toggles onto this effect so the
/// weapon receives the SAME look as the scene. It is gated the same way the world effect is
/// (compositor path only) so it never double-applies with the FSR2 <see cref="PostCanvasFx"/>
/// path, which already wraps the weapon from above the viewmodel CanvasLayer.
///
/// Lifecycle: <see cref="Attach"/> wires up the Compositor once after local player spawn;
/// <see cref="Configure"/> is called from Settings.ApplyEffects whenever graphics settings change.
/// </summary>
public static class ViewmodelMotionBlur
{
	/// <summary>Weapon motion blur is deliberately weaker than the world's (compositor.tres: 2.0,
	/// PostProcessEffect class default: 3.0): the muzzle's angular velocity during sway and fire/reload
	/// animations is far higher than typical world motion, so full-strength reconstruction smears the
	/// front of the gun into mush whenever the player moves while the stock stays sharp.</summary>
	private const float WeaponBlurStrength = 1.2f;

	private static PostProcessEffect _effect;

	/// <summary>The per-viewmodel PostProcessEffect, or null before <see cref="Attach"/>. Exposed so
	/// the ADS post-FX feed can push AdsBlend (vignette boost) onto the weapon pass too.</summary>
	public static PostProcessEffect Effect => _effect;

	/// <summary>Locates the viewmodel WorldEnvironment under the given LocalPlayer and attaches a Compositor with a PostProcessEffect. Idempotent — calling twice replaces the previous attachment. Effect toggles are left at defaults; <see cref="Configure"/> sets the real state right after.</summary>
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

	/// <summary>Mirrors the world PostProcessEffect's toggles onto the per-viewmodel effect so the
	/// weapon gets the same CA / sharpen / vignette / grain / motion blur as the world. No-op if not
	/// yet attached. <paramref name="enabled"/> must follow the world effect's gating (compositor path
	/// only) so this never stacks on top of the FSR2 PostCanvasFx pass.</summary>
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

	/// <summary>True if the node lives inside an own_world_3d SubViewport (= the weapon viewmodel's
	/// isolated world). Lets the world-env finders skip the viewmodel's own Environment now that it
	/// carries a Compositor (which would otherwise be mistaken for the level's WorldEnvironment).</summary>
	public static bool IsViewmodelEnvironment(Node node)
	{
		for (Node n = node; n != null; n = n.GetParent())
			if (n is SubViewport sv && sv.OwnWorld3D) return true;
		return false;
	}

	/// <summary>Walks the LocalPlayer subtree looking for a SubViewport with own_world_3d (= the weapon viewmodel viewport) and returns its WorldEnvironment child.</summary>
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
