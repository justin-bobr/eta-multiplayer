using Godot;

namespace Vantix.Fx;

/// <summary>
/// Drives the local player's team-glow SubViewport pipeline. Each frame it keeps the body/text glow
/// cameras locked to the FPS camera (transform + FOV/near/far) and matches the SubViewport size to the
/// main viewport for 1:1 sampling, and toggles the glow CanvasLayer from Settings.TeamGlow.
/// On _Ready it rebinds the composite body_tex/text_tex from live ViewportTextures (<see cref="RebindCompositeTextures"/>)
/// and clones the world Environment for the glow cameras (<see cref="BuildGlowEnvironment"/>).
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

	/// <summary>Glow SubViewport render scale relative to the main viewport (1.0 = pixel-perfect).</summary>
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

	/// <summary>Binds the composite material's body_tex/text_tex to the live SubViewport textures, sidestepping the .tscn ViewportTexture path-resolution failure in subscenes.</summary>
	private void RebindCompositeTextures()
	{
		if (CompositeRect?.Material is not ShaderMaterial mat) return;
		if (BodyViewport != null) mat.SetShaderParameter("body_tex", BodyViewport.GetTexture());
		if (TextViewport != null) mat.SetShaderParameter("text_tex", TextViewport.GetTexture());
	}

	/// <summary>Builds the glow-camera Environment by duplicating the active world Environment (so all grading/atmospherics
	/// match even if it leaks), then forcing background = transparent Color and disabling glow/adjustment.
	/// Glow is disabled so bright team_color channels aren't bloomed back into body_tex and smear the silhouette.</summary>
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

	/// <summary>Returns the first WorldEnvironment's Environment found from the tree root, or null.</summary>
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
