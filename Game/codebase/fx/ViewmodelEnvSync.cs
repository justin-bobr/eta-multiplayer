using Godot;

/// <summary>
/// Copies the level WorldEnvironment's look (tonemap, colour adjustment + LUT, glow, ambient tint/energy)
/// onto the weapon viewmodel's own_world_3d Environment so the gun matches the loaded map.
/// Keeps the viewmodel-specific setup (Sky ambient/reflection from <see cref="WorldCaptureRig"/>, SSAO,
/// the <see cref="ViewmodelLightSampler"/> light rig). Called once from NetMain after the local player spawns.
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

		vm.TonemapMode = world.TonemapMode;
		vm.TonemapExposure = world.TonemapExposure;
		vm.TonemapWhite = world.TonemapWhite;

		vm.AdjustmentEnabled = world.AdjustmentEnabled;
		vm.AdjustmentBrightness = world.AdjustmentBrightness;
		vm.AdjustmentContrast = world.AdjustmentContrast;
		vm.AdjustmentSaturation = world.AdjustmentSaturation;
		vm.AdjustmentColorCorrection = world.AdjustmentColorCorrection;

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

		// Match ambient tint/energy but keep the viewmodel's Sky ambient source (fed by the capture rig).
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

	/// <summary>Returns the level's Environment (compositor-bearing WorldEnvironment, not a viewmodel own-world one), else the first non-viewmodel env.</summary>
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
