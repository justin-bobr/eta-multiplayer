using Godot;

/// <summary>
/// Drives the team-glow SubViewport pipeline on the local player. Three jobs each frame:
///
/// 1. Keep the body / text glow Camera3Ds in lock-step with the FPS camera (GlobalTransform, Fov,
///    Near, Far). Without this the composite would mis-register on ADS-zoom FOV snaps.
/// 2. Match the SubViewport pixel size to the main viewport so the canvas-item composite samples
///    1:1 texels with no aspect-ratio distortion.
/// 3. Poll Settings.TeamGlow and toggle the glow CanvasLayer's visibility, so the user can A/B
///    the team-glow live from the Settings menu.
///
/// On _Ready the script also rebinds body_tex / text_tex on the composite ColorRect's
/// ShaderMaterial from the SubViewports' live ViewportTextures (sidesteps the local_player.tscn-
/// is-a-subscene viewport_path lookup failure that previously produced the magenta missing-tex
/// placeholder), and clones the active WorldEnvironment's Environment for the glow cameras with
/// background_mode forced to Color + transparent + glow disabled — see <see cref="BuildGlowEnvironment"/>
/// for the rationale on duplicating instead of building from scratch.
/// </summary>
[Tool]
public partial class GlowViewportSync : Node
{
	[Export] public Camera3D MainCamera;
	[Export] public SubViewport BodyViewport;
	[Export] public Camera3D BodyCamera;
	[Export] public SubViewport TextViewport;
	[Export] public Camera3D TextCamera;
	[Export] public ColorRect CompositeRect;

	/// <summary>Render scale for the glow SubViewports relative to the main viewport. 1.0 =
	/// pixel-perfect outline at the cost of two extra full-res render passes (cheap because they
	/// only see layers 19 / 20 = puppet body clones + Label3Ds). 0.5 = quarter resolution for
	/// chunkier outlines on a tight perf budget.</summary>
	[Export(PropertyHint.Range, "0.25,1.0,0.05")] public float RenderScale = 1.0f;

	private Vector2I _lastSyncedSize = Vector2I.Zero;
	private bool _lastAppliedEnabled = true;

	public override void _Ready()
	{
		ProcessPriority = 200;
		if (Engine.IsEditorHint()) return;
		RebindCompositeTextures();
		BuildGlowEnvironment();
	}

	/// <summary>Pushes live SubViewport.GetTexture() into the composite ShaderMaterial's body_tex /
	/// text_tex uniforms. Called once in _Ready; the returned ViewportTexture stays valid for the
	/// lifetime of the SubViewport so no per-frame refresh is needed. Sidesteps the .tscn-baked
	/// ViewportTexture sub_resources whose viewport_path is anchored to the SCENE ROOT, which fails
	/// to resolve when local_player.tscn is loaded as a subscene (the magenta placeholder bug).</summary>
	private void RebindCompositeTextures()
	{
		if (CompositeRect?.Material is not ShaderMaterial mat) return;
		if (BodyViewport != null) mat.SetShaderParameter("body_tex", BodyViewport.GetTexture());
		if (TextViewport != null) mat.SetShaderParameter("text_tex", TextViewport.GetTexture());
	}

	/// <summary>Builds the per-camera Environment override for the two glow viewports by DUPLICATING
	/// the currently-active WorldEnvironment's Environment, then forcing background_mode = Color
	/// + transparent + glow_enabled = false. The duplicate guarantees every other value (tonemap_mode,
	/// adjustment_brightness, adjustment_color_correction LUT, ambient_light, ssao, ssr, sdfgi, fog…)
	/// is byte-identical to the world's. So even IF the env override leaks to the main camera due to
	/// a Godot shared-World3D quirk, the visible difference is limited to "no sky + no bloom" — the
	/// world's lighting / colour grading / atmospherics are preserved.
	///
	/// Without this duplicate-and-tweak approach, a hand-built minimal Environment would default
	/// dozens of fields to "off" (no tonemap, no adjustment, no LUT, no ambient) and any leak would
	/// flatten the main viewport to a pre-tonemap dark mess.
	///
	/// Why disable glow on the override:
	///   • The body_id shader writes ALBEDO = team_color with up to 1.0 in any channel. The world
	///     env has glow_hdr_threshold = 0.95 — any team colour with a channel ≥ 0.95 (blue team's
	///     blue = 1.00, green team's green = 0.95, etc.) WOULD be extracted by the glow pass and
	///     bloomed back into body_tex, smearing the silhouette. Disabling glow in the SubViewport
	///     keeps the body_id render crisp; the composite shader runs AFTER the main viewport's
	///     glow pass anyway so the world's bloom is untouched.</summary>
	private void BuildGlowEnvironment()
	{
		var sourceEnv = FindActiveWorldEnvironment();
		if (sourceEnv == null)
		{
			GD.PushWarning("[GlowViewportSync] no active WorldEnvironment found — glow cameras will use default Environment (sky may leak into body_tex)");
			return;
		}
		var glowEnv = (Environment)sourceEnv.Duplicate();
		glowEnv.BackgroundMode = Godot.Environment.BGMode.Color;
		glowEnv.BackgroundColor = new Color(0f, 0f, 0f, 0f);
		glowEnv.GlowEnabled = false;
		glowEnv.AdjustmentEnabled = false;
		if (BodyCamera != null) BodyCamera.Environment = glowEnv;
		if (TextCamera != null) TextCamera.Environment = glowEnv;
		GD.Print("[GlowViewportSync] glow cameras assigned cloned-from-world Environment with bg=Color/transparent + glow=off + adjustment=off + tonemap=Linear");
	}

	/// <summary>Walks the tree from the root to find the first WorldEnvironment node and returns
	/// its Environment resource. The world's map scene (de_dust2.tscn etc.) typically holds it as
	/// a child of the level root. Returns null if no WorldEnvironment is present anywhere — that
	/// usually means we're running in a stripped test scene without an env.</summary>
	private Godot.Environment FindActiveWorldEnvironment()
	{
		var root = GetTree()?.Root;
		if (root == null) return null;
		return WalkForWorldEnvironment(root)?.Environment;
	}

	private static WorldEnvironment WalkForWorldEnvironment(Node node)
	{
		if (node is WorldEnvironment we && we.Environment != null) return we;
		for (int i = 0; i < node.GetChildCount(); i++)
		{
			var found = WalkForWorldEnvironment(node.GetChild(i));
			if (found != null) return found;
		}
		return null;
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;
		using var _prof = MiniProfiler.SampleClient("GlowViewportSync._Process");
		if (MainCamera == null) return;

		bool enabled = Settings.TeamGlow;
		if (enabled != _lastAppliedEnabled)
		{
			_lastAppliedEnabled = enabled;
			if (GetParent() is CanvasLayer glowLayer) glowLayer.Visible = enabled;
		}
		if (!enabled) return;

		Transform3D xf = MainCamera.GlobalTransform;
		float fov = MainCamera.Fov;
		float near = MainCamera.Near;
		float far = MainCamera.Far;

		if (BodyCamera != null)
		{
			BodyCamera.GlobalTransform = xf;
			BodyCamera.Fov = fov;
			BodyCamera.Near = near;
			BodyCamera.Far = far;
		}
		if (TextCamera != null)
		{
			TextCamera.GlobalTransform = xf;
			TextCamera.Fov = fov;
			TextCamera.Near = near;
			TextCamera.Far = far;
		}

		Vector2I targetSize = (Vector2I)(GetViewport().GetVisibleRect().Size * RenderScale);
		if (targetSize.X < 32) targetSize.X = 32;
		if (targetSize.Y < 32) targetSize.Y = 32;
		if (targetSize != _lastSyncedSize)
		{
			_lastSyncedSize = targetSize;
			if (BodyViewport != null) BodyViewport.Size = targetSize;
			if (TextViewport != null) TextViewport.Size = targetSize;
		}
	}
}
