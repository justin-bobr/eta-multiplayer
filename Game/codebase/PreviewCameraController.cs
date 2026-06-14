using System.Collections.Generic;
using Godot;

namespace Vantix;

/// <summary>Cycles through the map's preview <see cref="Camera3D"/>s (<see cref="Level.PreviewCams"/>) while the
/// LocalPlayer hasn't spawned. Each is shown for <see cref="DwellSec"/>, then cross-fades to the next: a frozen
/// snapshot of the current viewport is parked on a top TextureRect while the camera switches underneath, and the
/// overlay alpha lerps 1→0 over <see cref="CutFadeSec"/> — no black flash. Auto-retires when a non-preview
/// camera becomes current.</summary>
public partial class PreviewCameraController : Node
{
	[Export]
	public float DwellSec = 10.0f;

	[Export]
	public float CutFadeSec = 1.20f;

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
			GD.PushWarning("[PreviewCam] Level has no PreviewCams — controller disabled");
			QueueFree();
			return;
		}
		ActivateCam(0);
		_dwellTimer = DwellSec;
		// SceneLoader leaves the overlay opaque-black after the scene switch; fade it off to reveal the first camera.
		WorldFadeOverlay.Instance?.RequestFadeOut(0.25f);
	}

	public override void _Process(double delta)
	{
		if (_retired)
			return;
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
			if (_crossfadeRect != null)
				_crossfadeRect.Modulate = new Color(1f, 1f, 1f, alpha);
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

	/// <summary>Snapshots the viewport onto a top-layer TextureRect, then switches the camera underneath;
	/// <see cref="_Process"/> drives the alpha-lerp from 1→0.</summary>
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

		// GPU→CPU readback (~1-3ms), but only once per ~10s dwell, so negligible.
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

	/// <summary>Pulls the preview cameras from <see cref="Level.PreviewCams"/>. Re-invokable if the map changes.</summary>
	public void RefreshCameraList()
	{
		_cams.Clear();
		var level = World.Level;
		if (level == null)
			return;
		foreach (Camera3D c in level.PreviewCams)
			if (GodotObject.IsInstanceValid(c))
				_cams.Add(c);
	}

	private void ActivateCam(int idx)
	{
		if (_cams.Count == 0)
			return;
		_index = idx;
		for (int i = 0; i < _cams.Count; i++)
			_cams[i].Current = i == idx;
	}
}
