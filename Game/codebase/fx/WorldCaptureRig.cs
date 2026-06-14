using Godot;

namespace Vantix.Fx;

/// <summary>
/// Live position-aware reflection cubemap for the own_world_3d weapon viewmodel, which can't see the
/// main world's probes/GI/Sky. Hosts 6 SubViewport+Camera3D pairs at the player position facing each
/// cube direction (world cull mask only, so the gun doesn't self-reflect), refreshes one face per
/// FaceUpdateInterval frames round-robin, and feeds each ViewportTexture to <c>viewmodel_cube_sky.gdshader</c>.
/// Face order MUST match the shader's face_* uniforms: px, nx, py, ny, pz, nz. Cameras are repositioned
/// each frame but stay axis-aligned (player turning must not rotate the cube). The Sky process_mode must be REALTIME.
/// </summary>
public partial class WorldCaptureRig : Node3D
{
	/// <summary>Camera the cube is positioned at each frame (typically fps_camera). Anchor rotation is not applied.</summary>
	[Export] public Camera3D AnchorCamera;

	/// <summary>The 6 cube-face SubViewports, order +X, -X, +Y, -Y, +Z, -Z, each with one Camera3D child.</summary>
	[Export] public Godot.Collections.Array<SubViewport> Faces = new();

	/// <summary>Sky whose sky_material (viewmodel_cube_sky.gdshader) receives the 6 ViewportTextures at _Ready.</summary>
	[Export] public Sky ViewmodelSky;

	/// <summary>Cube-camera cull mask; defaults to world layer 1, excluding the viewmodel so the gun doesn't self-reflect.</summary>
	[Export(PropertyHint.Layers3DRender)] public uint CaptureCullMask = 1;

	/// <summary>Frames between single-face updates (higher = cheaper). Throttled and gated behind Settings.Reflections; flat-out it's a measured 300->30 FPS hit.</summary>
	[Export(PropertyHint.Range, "1,16,1")] public int FaceUpdateInterval = 4;
	private int _frameAccum;

	/// <summary>Cube-camera far plane, kept short: it feeds a 64px IBL, and a large far re-renders the whole map (incl. directional shadows) per face.</summary>
	[Export(PropertyHint.Range, "20,500,5")] public float CaptureFar = 80f;

	private static readonly Vector3[] _faceRotationsDeg = new[]
	{
		new Vector3(0, -90, 0),    // +X (yaw right)
		new Vector3(0, 90, 0),     // -X (yaw left)
		new Vector3(-90, 0, 0),    // +Y (pitch up)
		new Vector3(90, 0, 0),     // -Y (pitch down)
		new Vector3(0, 180, 0),    // +Z (yaw back)
		new Vector3(0, 0, 0),      // -Z (default forward)
	};

	private static readonly StringName[] _uniformNames =
	{
		"face_px", "face_nx", "face_py", "face_ny", "face_pz", "face_nz"
	};

	private readonly Camera3D[] _faceCams = new Camera3D[6];
	private int _currentFace;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;

		ShaderMaterial sm = ViewmodelSky?.SkyMaterial as ShaderMaterial;

		for (int i = 0; i < 6; i++)
		{
			if (i >= Faces.Count) break;
			SubViewport vp = Faces[i];
			if (vp == null) continue;

			// Disabled + per-frame flip to Once is the round-robin update idiom.
			vp.RenderTargetUpdateMode = Godot.SubViewport.UpdateMode.Disabled;
			vp.RenderTargetClearMode = SubViewport.ClearMode.Always;

			foreach (Node c in vp.GetChildren())
			{
				if (c is Camera3D cc) { _faceCams[i] = cc; break; }
			}

			if (_faceCams[i] != null)
			{
				_faceCams[i].RotationDegrees = _faceRotationsDeg[i];
				_faceCams[i].Fov = 90.0f;
				_faceCams[i].Near = 0.05f;
				_faceCams[i].Far = CaptureFar;
				_faceCams[i].CullMask = CaptureCullMask;
				_faceCams[i].Current = true;
			}
			// Keep a minimal atlas, not 0: shadow-casting omni lights in view hit "framebuffer is null" draw_list errors without one.
			vp.PositionalShadowAtlasSize = 256;

			if (sm != null)
				sm.SetShaderParameter(_uniformNames[i], vp.GetTexture());
		}
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;
		if (AnchorCamera == null) return;
		using var _prof = MiniProfiler.SampleClient("WorldCaptureRig._Process");
		if (!Settings.Reflections || !Settings.WeaponLight) return;

		// Position all 6 cube cameras at the anchor each frame; rotation stays axis-aligned.
		Vector3 anchorPos = AnchorCamera.GlobalPosition;
		for (int i = 0; i < 6; i++)
		{
			if (_faceCams[i] != null)
				_faceCams[i].GlobalPosition = anchorPos;
		}

		// Round-robin one face every FaceUpdateInterval frames; IBL convolution hides the throttle.
		_frameAccum++;
		if (_frameAccum < FaceUpdateInterval) return;
		_frameAccum = 0;

		if (_currentFace < Faces.Count && Faces[_currentFace] != null)
			Faces[_currentFace].RenderTargetUpdateMode = Godot.SubViewport.UpdateMode.Once;
		_currentFace = (_currentFace + 1) % 6;
	}
}
