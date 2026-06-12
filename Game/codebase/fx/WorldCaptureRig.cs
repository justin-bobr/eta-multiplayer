using Godot;

/// <summary>
/// Phase-3b world capture for first-person weapon reflections.
///
/// The viewmodel viewport has own_world_3d = true, so it can't see the main world's
/// ReflectionProbes / GI / Sky. To still give the gun realistic reflections of the
/// surrounding geometry (deck, walls, tunnel ceiling…), this rig:
///
///   1. Hosts 6 SubViewports + Camera3D pairs at the player's position, each facing
///      a cube direction (±X, ±Y, ±Z). Cameras render the WORLD only (cull mask = 1)
///      so the gun doesn't self-reflect.
///   2. Updates one face per frame in round-robin order (= ~10 Hz full refresh at 60 fps,
///      a single small viewport per frame is cheap).
///   3. Feeds each face's ViewportTexture into the viewmodel viewport's Sky shader as
///      6 sampler2D uniforms. The shader (<c>viewmodel_cube_sky.gdshader</c>) picks the
///      correct face per reflection direction, effectively giving the IBL pipeline a
///      live, position-aware cubemap.
///
/// Notes:
///   * Face rotation order in the array MUST match the shader's face_* uniform order:
///     px, nx, py, ny, pz, nz.
///   * Cube cameras are positioned each frame via direct GlobalPosition assignment
///     (SubViewport isn't a 3D parent, so cameras don't inherit transform). Their
///     ROTATION stays fixed at axis-aligned face directions — turning the player
///     should NOT rotate the cube.
///   * Sky resource's process_mode must be REALTIME (= 2) so the IBL radiance map
///     rebuilds each frame from the changing texture inputs.
/// </summary>
public partial class WorldCaptureRig : Node3D
{
	/// <summary>Where the cube cameras are positioned. Typically the main fps_camera so the
	/// cubemap captures the player's surroundings. Position is tracked each frame; rotation
	/// from the anchor is NOT applied (cube faces are world-axis-aligned).</summary>
	[Export] public Camera3D AnchorCamera;

	/// <summary>The 6 SubViewports for the cube faces. Order: +X, -X, +Y, -Y, +Z, -Z.
	/// Each SubViewport must contain a single Camera3D child.</summary>
	[Export] public Godot.Collections.Array<SubViewport> Faces = new();

	/// <summary>The Sky resource whose sky_material is a ShaderMaterial using
	/// viewmodel_cube_sky.gdshader. We push the 6 ViewportTextures onto it at _Ready.</summary>
	[Export] public Sky ViewmodelSky;

	/// <summary>Render-layer mask for the cube cameras. Default = layer 1 (= World), excluding
	/// layer 2 (FPS / viewmodel) so the gun doesn't appear in its own reflection cube.</summary>
	[Export(PropertyHint.Layers3DRender)] public uint CaptureCullMask = 1;

	/// <summary>Frames between single-face updates. 1 = one face/frame (full cube every 6 frames,
	/// ~10 Hz at 60 fps — most reactive but ~1 extra world-render per frame). Higher = cheaper: 2 =
	/// full cube every 12 frames (~5 Hz), still smooth because IBL convolution is blurry and the
	/// player rarely needs sub-100 ms reflection latency on a gun. This rig was a measured 300→30 FPS
	/// hit when running flat-out, so it is throttled AND gated behind Settings.Reflections.</summary>
	[Export(PropertyHint.Range, "1,16,1")] public int FaceUpdateInterval = 4;
	private int _frameAccum;

	/// <summary>Far plane for the 6 cube cameras. Short on purpose: the cubemap feeds a 64px IBL for the
	/// viewmodel, so geometry beyond ~80m contributes nothing visible — but the old 500m far made every
	/// face render cull + submit the whole map incl. a per-viewport directional-shadow re-render
	/// (de_dust2: ~15ms steady main-thread cost + spikes). The decisive lever on heavy maps.</summary>
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

			// Render-target needs to stay alive every frame even though we only update one
			// face at a time — otherwise GetTexture() would give stale data. Setting to
			// Disabled + manually flipping to Once each frame is the round-robin idiom.
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
				_faceCams[i].Current = true;  // make it the active cam for its viewport
			}
			// NOTE: do NOT set PositionalShadowAtlasSize = 0 here — with shadow-casting omni lights in a
			// face's view, 4.6's renderer hits "framebuffer is null" draw_list errors on the missing atlas.
			vp.PositionalShadowAtlasSize = 256;   // minimal atlas: cheap, avoids the null-framebuffer path

			if (sm != null)
				sm.SetShaderParameter(_uniformNames[i], vp.GetTexture());
		}
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;
		if (AnchorCamera == null) return;
		// Shares the Reflections graphics toggle with the ReflectionProbes, and the Weapon Light debug
		// toggle kills the whole viewmodel light/reflection pipeline (sampler + this rig) in one switch.
		if (!Settings.Reflections || !Settings.WeaponLight) return;

		// Position all 6 cube cameras at the player camera's world position. Rotation
		// remains axis-aligned per face. Done every frame so reflections track the
		// player as they move.
		Vector3 anchorPos = AnchorCamera.GlobalPosition;
		for (int i = 0; i < 6; i++)
		{
			if (_faceCams[i] != null)
				_faceCams[i].GlobalPosition = anchorPos;
		}

		// Round-robin one face every FaceUpdateInterval frames. The IBL radiance convolution
		// averages enough that a throttled refresh isn't visible as flicker — fast-moving
		// reflections (player turns) stay smooth because the cubemap covers all directions, only
		// the contents shift. Throttling keeps the average extra world-render cost well below
		// one-per-frame.
		_frameAccum++;
		if (_frameAccum < FaceUpdateInterval) return;
		_frameAccum = 0;

		if (_currentFace < Faces.Count && Faces[_currentFace] != null)
			Faces[_currentFace].RenderTargetUpdateMode = Godot.SubViewport.UpdateMode.Once;
		_currentFace = (_currentFace + 1) % 6;
	}
}
