using Godot;

/// <summary>
/// Attaches a Compositor with an MB-only <see cref="PostProcessEffect"/> instance to
/// the weapon viewmodel's WorldEnvironment. The weapon lives in its own SubViewport
/// (transparent_bg, own_world_3d) which means the WORLD's Compositor never sees the
/// weapon's velocity buffer — so without a dedicated effect, weapon-motion-blur is
/// missing in CoD-style FPS feel.
///
/// All non-MB effects (heat haze, CA, sharpen, vignette, grain) stay off on this
/// effect — those are handled by the world-side path and would double-apply to the
/// weapon otherwise. Just the velocity-buffer reconstruction motion blur is what's
/// useful here.
///
/// Lifecycle: <see cref="Attach"/> wires up the Compositor once after local player
/// spawn. <see cref="SetEnabled"/> toggles the effect at runtime as Settings change
/// (e.g. disabled on FSR2 mode, or when the user turns off Motion Blur).
/// </summary>
public static class ViewmodelMotionBlur
{
	private static PostProcessEffect _effect;

	/// <summary>Locates the viewmodel WorldEnvironment under the given LocalPlayer and attaches a Compositor with an MB-only PostProcessEffect. Idempotent — calling twice replaces the previous attachment.</summary>
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
		_effect = new PostProcessEffect
		{
			HeatHaze = false,
			ChromaticAberration = false,
			Sharpening = false,
			Vignette = false,
			FilmGrain = false,
			MotionBlur = true,
			Enabled = true,
		};
		comp.CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _effect };
		vmEnv.Compositor = comp;
		Dbg.Print("[ViewmodelMotionBlur] attached to viewmodel_env");
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
