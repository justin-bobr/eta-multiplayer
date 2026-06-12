using Godot;

/// <summary>
/// Copies the active level's colour-grading + ambient mood from its WorldEnvironment onto the
/// weapon viewmodel's own Environment, so the gun "self-calibrates" to whatever map is loaded
/// (warm dust2 amber, cold night map, …) instead of staying on its hardcoded authored look.
///
/// The viewmodel viewport has own_world_3d, so it carries a SEPARATE Environment that does not
/// share the level's lighting / GI / reflection probes. We sync the values that define the
/// *look* — tonemap, colour adjustment + LUT, glow — plus the ambient tint/energy, while KEEPING
/// the viewmodel-specific setup that makes the gun readable: its Sky-based ambient/reflection
/// source (fed live by <see cref="WorldCaptureRig"/>), SSAO, and the 3-point light rig driven by
/// <see cref="ViewmodelLightSampler"/>.
///
/// Called once from NetMain after the local player spawns (the level env exists by then).
/// </summary>
public static class ViewmodelEnvSync
{
	/// <summary>Syncs viewmodel_env's look from the level WorldEnvironment. No-op if either env is missing.</summary>
	public static void Sync(Node localPlayer, SceneTree tree)
	{
		if (localPlayer == null || tree?.Root == null) return;
		Environment vm = FindViewmodelEnv(localPlayer);
		Environment world = FindWorldEnv(tree);
		if (vm == null || world == null)
		{
			Dbg.Print($"[ViewmodelEnvSync] skipped — vmEnv={(vm != null)} worldEnv={(world != null)}");
			return;
		}

		// Tonemap — the single biggest factor in "does the gun's brightness match the scene".
		vm.TonemapMode = world.TonemapMode;
		vm.TonemapExposure = world.TonemapExposure;
		vm.TonemapWhite = world.TonemapWhite;

		// Colour adjustment + LUT — the world's colour grade defines the map's mood.
		vm.AdjustmentEnabled = world.AdjustmentEnabled;
		vm.AdjustmentBrightness = world.AdjustmentBrightness;
		vm.AdjustmentContrast = world.AdjustmentContrast;
		vm.AdjustmentSaturation = world.AdjustmentSaturation;
		vm.AdjustmentColorCorrection = world.AdjustmentColorCorrection;

		// Glow — so the weapon's bloom threshold/strength/level-mix matches the world.
		vm.GlowEnabled = world.GlowEnabled;
		for (int i = 0; i < 7; i++) vm.SetGlowLevel(i, world.GetGlowLevel(i));
		vm.GlowNormalized = world.GlowNormalized;
		vm.GlowIntensity = world.GlowIntensity;
		vm.GlowStrength = world.GlowStrength;
		vm.GlowMix = world.GlowMix;
		vm.GlowBloom = world.GlowBloom;
		vm.GlowBlendMode = world.GlowBlendMode;
		vm.GlowHdrThreshold = world.GlowHdrThreshold;
		vm.GlowHdrScale = world.GlowHdrScale;
		vm.GlowHdrLuminanceCap = world.GlowHdrLuminanceCap;

		// Ambient tint/energy — KEEP the viewmodel's ambient SOURCE (Sky, fed by the capture rig)
		// but match the fixed-colour half + energy to the world so unlit faces read the right hue.
		vm.AmbientLightColor = world.AmbientLightColor;
		vm.AmbientLightEnergy = world.AmbientLightEnergy;

		Dbg.Print("[ViewmodelEnvSync] synced viewmodel_env <- world env (tonemap + adjustment + glow + ambient)");
	}

	/// <summary>Returns the Environment of the own_world_3d SubViewport under the local player (= the weapon viewmodel env).</summary>
	private static Environment FindViewmodelEnv(Node root)
	{
		foreach (Node n in root.FindChildren("*", "SubViewport", true, false))
		{
			if (n is not SubViewport sv || !sv.OwnWorld3D) continue;
			foreach (Node c in sv.GetChildren())
				if (c is WorldEnvironment we && we.Environment != null) return we.Environment;
		}
		return null;
	}

	/// <summary>Returns the level's Environment — the compositor-bearing WorldEnvironment that is NOT
	/// inside a viewmodel/own-world SubViewport. Falls back to the first non-viewmodel env found.</summary>
	private static Environment FindWorldEnv(SceneTree tree)
	{
		Environment first = null;
		foreach (Node n in tree.Root.FindChildren("*", "WorldEnvironment", true, false))
		{
			if (n is not WorldEnvironment we || we.Environment == null) continue;
			if (ViewmodelMotionBlur.IsViewmodelEnvironment(we)) continue;
			if (we.Compositor != null) return we.Environment;
			first ??= we.Environment;
		}
		return first;
	}
}
