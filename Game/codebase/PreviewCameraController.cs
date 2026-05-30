using Godot;
using System.Collections.Generic;

/// <summary>
/// Cycles through every <see cref="Camera3D"/> in the Godot group "preview_cam" while the LocalPlayer
/// hasn't spawned yet (competitive team-select phase, or spectator-mode). Each camera is shown for
/// <see cref="DwellSec"/> seconds, then a smooth cross-fade transitions to the next:
///
/// 1. Capture the current viewport texture into a frozen ImageTexture.
/// 2. Display it as a full-screen TextureRect overlay (above the world).
/// 3. Switch the active camera underneath (already rendering the new angle).
/// 4. Lerp the overlay's alpha from 1 → 0 over <see cref="CutFadeSec"/> seconds.
/// 5. Free the overlay.
///
/// Result: the old image dissolves into the new one — no black flash. The viewport's
/// <see cref="Viewport.GetTexture"/>.<see cref="ViewportTexture.GetImage"/> call is a GPU→CPU
/// readback (a few ms), but it happens once every ~10s during the team-select phase so the
/// cost is negligible.
///
/// Auto-retires (free's itself) when a non-preview camera becomes current (= LocalPlayer's
/// fps_camera took over after SpawnAuthorize).
/// </summary>
public partial class PreviewCameraController : Node
{
	[Export] public float DwellSec = 10.0f;
	[Export] public float CutFadeSec = 1.20f;
	[Export] public StringName GroupName = new("preview_cam");

	private readonly List<Camera3D> _cams = new();
	private int _index;
	private float _dwellTimer;
	private bool _fading;
	private float _fadeRemaining;
	private CanvasLayer _crossfadeLayer;
	private TextureRect _crossfadeRect;
	private bool _retired;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		RefreshCameraList();
		if (_cams.Count == 0)
		{
			GD.PushWarning("[PreviewCam] No nodes in group \"" + GroupName + "\" — controller disabled");
			QueueFree();
			return;
		}
		ActivateCam(0);
		_dwellTimer = DwellSec;
		// SceneLoader leaves the WorldFadeOverlay opaque-black after switching scenes — fade it
		// off here so the first preview camera is revealed smoothly. Fast fade so the user
		// doesn't sit on black for a noticeable beat.
		WorldFadeOverlay.Instance?.RequestFadeOut(0.25f);
	}

	public override void _Process(double delta)
	{
		if (_retired) return;
		// Auto-retire as soon as the LocalPlayer's camera takes over.
		Camera3D active = GetViewport()?.GetCamera3D();
		if (active != null && !_cams.Contains(active))
		{
			_retired = true;
			CleanupCrossfade();
			QueueFree();
			return;
		}

		if (_fading)
		{
			_fadeRemaining -= (float)delta;
			float alpha = Mathf.Clamp(_fadeRemaining / CutFadeSec, 0f, 1f);
			if (_crossfadeRect != null) _crossfadeRect.Modulate = new Color(1f, 1f, 1f, alpha);
			if (_fadeRemaining <= 0f)
			{
				CleanupCrossfade();
				_fading = false;
				_dwellTimer = DwellSec;
			}
			return;
		}

		_dwellTimer -= (float)delta;
		if (_dwellTimer <= 0f && _cams.Count > 1)
			BeginCrossfade();
	}

	/// <summary>Snapshots the current viewport, parks the snapshot on a top-layer TextureRect, then switches the camera underneath. Caller drives the alpha-lerp from 1 → 0 in <see cref="_Process"/>.</summary>
	private void BeginCrossfade()
	{
		var vp = GetViewport();
		if (vp == null)
		{
			// No viewport (shouldn't happen at runtime) → just hard-switch.
			int next = (_index + 1) % _cams.Count;
			ActivateCam(next);
			_dwellTimer = DwellSec;
			return;
		}

		// GPU→CPU readback of the current frame. Expensive (~1-3ms) but only fires once per
		// dwell period (~every 10s), so it's invisible cost-wise.
		var img = vp.GetTexture().GetImage();
		if (img == null)
		{
			int next = (_index + 1) % _cams.Count;
			ActivateCam(next);
			_dwellTimer = DwellSec;
			return;
		}
		var frozen = ImageTexture.CreateFromImage(img);

		_crossfadeLayer = new CanvasLayer { Layer = 990 };
		_crossfadeRect = new TextureRect
		{
			Texture = frozen,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_crossfadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_crossfadeLayer.AddChild(_crossfadeRect);
		AddChild(_crossfadeLayer);

		int nextIdx = (_index + 1) % _cams.Count;
		ActivateCam(nextIdx);
		_fading = true;
		_fadeRemaining = CutFadeSec;
	}

	private void CleanupCrossfade()
	{
		if (_crossfadeLayer != null && GodotObject.IsInstanceValid(_crossfadeLayer))
		{
			_crossfadeLayer.QueueFree();
		}
		_crossfadeLayer = null;
		_crossfadeRect = null;
	}

	/// <summary>Re-scans the scene tree for cameras in <see cref="GroupName"/>. Called once on _Ready; can be invoked from outside if cameras are added/removed at runtime.</summary>
	public void RefreshCameraList()
	{
		_cams.Clear();
		foreach (Node n in GetTree().GetNodesInGroup(GroupName))
		{
			if (n is Camera3D c && GodotObject.IsInstanceValid(c)) _cams.Add(c);
		}
	}

	private void ActivateCam(int idx)
	{
		if (_cams.Count == 0) return;
		_index = idx;
		for (int i = 0; i < _cams.Count; i++)
			_cams[i].Current = i == idx;
	}
}
