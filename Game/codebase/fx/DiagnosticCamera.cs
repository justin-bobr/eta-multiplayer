using Godot;

/// <summary>
/// Fixed-position camera used to isolate the world-render pipeline from any
/// player/viewmodel-side influence AND optionally from the WorldEnvironment's
/// Compositor + Environment overrides. When <see cref="DiagEnabled"/> is true
/// the camera re-asserts Current=true every _Process so the LocalPlayer /
/// spectate camera can't reclaim focus mid-frame.
///
/// <see cref="HideViewmodel"/> hides the viewmodel SubViewportContainer so the
/// weapon doesn't composit on top with its own Environment / Compositor /
/// motion-blur pipeline.
///
/// <see cref="BypassCompositor"/> attaches an empty Compositor to THIS camera,
/// which (per Godot 4.3+ Camera3D override behaviour) overrides the
/// WorldEnvironment's compositor → no PostProcessEffect runs for this view.
/// That bypasses CA, Sharpen, Vignette, Grain, MotionBlur in one go.
///
/// <see cref="BypassEnvironment"/> attaches an empty Environment to this camera
/// → no tonemap_white, no glow, no SSR/SSIL/SSAO, no AdjustmentBrightness, no
/// LUT, no fog. The render goes through raw without any environment post-step.
///
/// Bisection strategy: enable Diag + HideViewmodel → if the artefact persists,
/// flip BypassCompositor on → if it then disappears the Compositor is the
/// source; otherwise flip BypassEnvironment on → if it disappears the
/// Environment is the source; if both off and the artefact STILL persists,
/// it's in the world itself (materials / lightmap / probes / lights).
/// </summary>
[Tool]
public partial class DiagnosticCamera : Camera3D
{
	/// <summary>Toggle on/off at runtime. When true, this camera force-claims rendering every frame.</summary>
	[Export] public bool DiagEnabled = true;

	/// <summary>Hide the viewmodel SubViewportContainer while diagnostic mode is on. Restores its previous visibility when diagnostic mode flips off so the player view returns to normal.</summary>
	[Export] public bool HideViewmodel = true;

	/// <summary>Attach an empty Compositor to this camera, overriding the WorldEnvironment compositor (CA / Sharpen / Vignette / Grain / MotionBlur all bypassed for this view). When false, this camera uses the global WorldEnvironment compositor like normal.</summary>
	[Export] public bool BypassCompositor = false;

	/// <summary>Attach an empty Environment to this camera, overriding the WorldEnvironment env (tonemap, glow, SSR/SSIL/SSAO, adjustment-brightness, fog, LUT all bypassed). Raw shading-only render. When false, this camera uses the global WorldEnvironment.</summary>
	[Export] public bool BypassEnvironment = false;

	private SubViewportContainer _viewmodelContainer;
	private bool _viewmodelOriginalVisible = true;
	private bool _viewmodelCached;

	private Compositor _emptyCompositor;
	private Environment _emptyEnvironment;

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;

		if (DiagEnabled)
		{
			if (!Current) Current = true;
			if (HideViewmodel) ApplyHideViewmodel();
			ApplyCompositorBypass();
			ApplyEnvironmentBypass();
		}
		else
		{
			if (_viewmodelCached) RestoreViewmodel();
			if (Compositor != null) Compositor = null;
			if (Environment != null) Environment = null;
		}
	}

	/// <summary>Toggles the override compositor on/off based on <see cref="BypassCompositor"/>. Allocates a single empty Compositor once and reuses it.</summary>
	private void ApplyCompositorBypass()
	{
		if (BypassCompositor)
		{
			_emptyCompositor ??= new Compositor();
			if (Compositor != _emptyCompositor) Compositor = _emptyCompositor;
		}
		else
		{
			if (Compositor != null) Compositor = null;
		}
	}

	/// <summary>Toggles the override environment on/off based on <see cref="BypassEnvironment"/>. Empty Environment = engine defaults (tonemap Linear, no glow, no screen-space effects, no adjustments).</summary>
	private void ApplyEnvironmentBypass()
	{
		if (BypassEnvironment)
		{
			_emptyEnvironment ??= new Environment();
			if (Environment != _emptyEnvironment) Environment = _emptyEnvironment;
		}
		else
		{
			if (Environment != null) Environment = null;
		}
	}

	/// <summary>Walks the tree once to find the player's viewmodel SubViewportContainer, caches its scene-default Visible state, then hides it. Re-called every frame while diagnostic mode is on so a respawn that re-instantiates the container is also caught.</summary>
	private void ApplyHideViewmodel()
	{
		if (_viewmodelContainer == null || !GodotObject.IsInstanceValid(_viewmodelContainer))
		{
			_viewmodelContainer = FindViewmodelContainer();
			_viewmodelCached = false;
			if (_viewmodelContainer == null) return;
		}
		if (!_viewmodelCached)
		{
			_viewmodelOriginalVisible = _viewmodelContainer.Visible;
			_viewmodelCached = true;
		}
		if (_viewmodelContainer.Visible) _viewmodelContainer.Visible = false;
	}

	/// <summary>Restores the viewmodel container's original visibility (typically true) when DiagEnabled flips off.</summary>
	private void RestoreViewmodel()
	{
		if (_viewmodelContainer != null && GodotObject.IsInstanceValid(_viewmodelContainer))
			_viewmodelContainer.Visible = _viewmodelOriginalVisible;
		_viewmodelCached = false;
		_viewmodelContainer = null;
	}

	/// <summary>Locates the first SubViewportContainer named "viewmodel_container" anywhere in the tree. There is only one local player at a time so the first hit is the right one.</summary>
	private SubViewportContainer FindViewmodelContainer()
	{
		foreach (Node n in GetTree().Root.FindChildren("viewmodel_container", "SubViewportContainer", true, false))
		{
			if (n is SubViewportContainer svc) return svc;
		}
		return null;
	}
}
