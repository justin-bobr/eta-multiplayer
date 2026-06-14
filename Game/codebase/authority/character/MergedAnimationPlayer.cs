using Godot;
using System;
using System.Collections.Generic;

namespace Vantix.Character;

[Tool, GlobalClass]
public partial class MergedAnimationPlayer : AnimationPlayer
{
	[Export] public NodePath SkeletonPath { get; set; }
	[Export(PropertyHint.Dir)] public string AnimationsFolder { get; set; } = "";
	[Export] public Vector3 RootRotationOffsetDegrees { get; set; } = Vector3.Zero;

	[Export]
	public bool Bake
	{
		get => false;
		set { if (value) StartBake(); }
	}

	[Export]
	public bool CancelBake
	{
		get => false;
		set { if (value) Stop("cancelled"); }
	}

	[Export] public string Status { get; set; } = "";
	[Export(PropertyHint.Range, "1,10,1")] public int FilesPerFrame = 1;

	private enum Phase { Idle, Bake, Emit }
	private Phase _phase = Phase.Idle;

	private Skeleton3D _skeleton;
	private NodePath _skeletonRelative;
	private Dictionary<string, AnimationLibrary> _pendingLibs;
	private List<(string lib, string file)> _filesToProcess;
	private int _fileIdx;
	private int _existingAnimCount;
	private float _statusDoneTimer;
	private bool _axisFixLogged;

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint())
			return;
		switch (_phase)
		{
			case Phase.Bake:
				StepBake();
				break;
			case Phase.Emit:
				StepEmit();
				break;
		}
		if (_statusDoneTimer > 0f)
		{
			_statusDoneTimer -= (float)delta;
			if (_statusDoneTimer <= 0f)
				Status = "";
		}
	}

	private void StartBake()
	{
		if (_phase != Phase.Idle)
			return;
		if (SkeletonPath == null || SkeletonPath.IsEmpty)
		{ Fail("SkeletonPath is not set"); return; }
		_skeleton = GetNodeOrNull<Skeleton3D>(SkeletonPath);
		if (_skeleton == null)
		{ Fail($"No Skeleton3D at '{SkeletonPath}'"); return; }
		if (string.IsNullOrEmpty(AnimationsFolder))
		{ Fail("AnimationsFolder is not set"); return; }
		var rootDir = DirAccess.Open(AnimationsFolder);
		if (rootDir == null)
		{ Fail($"Cannot open folder '{AnimationsFolder}'"); return; }

		var animRoot = GetNodeOrNull(RootNode);
		if (animRoot == null)
		{ Fail("AnimationPlayer.RootNode does not resolve"); return; }
		_skeletonRelative = animRoot.GetPathTo(_skeleton);

		_filesToProcess = new List<(string, string)>();
		foreach (string sub in rootDir.GetDirectories())
		{
			string subFull = $"{AnimationsFolder.TrimEnd('/')}/{sub}";
			var subDir = DirAccess.Open(subFull);
			if (subDir == null)
				continue;
			foreach (string file in subDir.GetFiles())
				if (IsAnimationFile(file))
					_filesToProcess.Add((sub, file));
		}
		if (_filesToProcess.Count == 0)
		{ Fail("no .fbx / .glb / .gltf / .tres files in any subfolder"); return; }

		_existingAnimCount = 0;
		foreach (StringName name in GetAnimationLibraryList())
		{
			var prior = GetAnimationLibrary(name);
			if (prior != null)
				_existingAnimCount += prior.GetAnimationList().Count;
		}
		foreach (StringName name in GetAnimationLibraryList())
			RemoveAnimationLibrary(name);

		_pendingLibs = new Dictionary<string, AnimationLibrary>();
		_axisFixLogged = false;
		foreach (var (libName, _) in _filesToProcess)
		{
			if (!_pendingLibs.ContainsKey(libName))
				_pendingLibs[libName] = new AnimationLibrary();
		}
		_fileIdx = 0;
		_statusDoneTimer = 0f;
		_phase = Phase.Bake;
		Status = $"LOAD 0/{_filesToProcess.Count} (0%)";
	}

	private void StepBake()
	{
		int stepEnd = Mathf.Min(_fileIdx + Mathf.Max(1, FilesPerFrame), _filesToProcess.Count);
		for (; _fileIdx < stepEnd; _fileIdx++)
		{
			var (libName, file) = _filesToProcess[_fileIdx];
			string subFolder = $"{AnimationsFolder.TrimEnd('/')}/{libName}";
			string fullPath = $"{subFolder}/{file}";
			var lib = _pendingLibs[libName];

			if (file.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
				ProcessTresFile(file, fullPath, lib);
			else
				ProcessSceneFile(file, fullPath, subFolder, lib);
		}

		int pct = _filesToProcess.Count > 0 ? (100 * _fileIdx) / _filesToProcess.Count : 100;
		string currentName = _fileIdx < _filesToProcess.Count ? $"{_filesToProcess[_fileIdx].lib}/{_filesToProcess[_fileIdx].file}" : "";
		Status = $"LOAD {_fileIdx}/{_filesToProcess.Count} ({pct}%) {currentName}";

		if (_fileIdx >= _filesToProcess.Count)
			_phase = Phase.Emit;
	}

	private void ProcessTresFile(string file, string fullPath, AnimationLibrary lib)
	{
		var src = ResourceLoader.Load<Animation>(fullPath);
		if (src == null)
		{
			GD.PushWarning($"[MergedAnimationPlayer] Skipped '{fullPath}' — Animation load returned null");
			return;
		}
		RetargetTrackPaths(src, _skeletonRelative);
		ApplyAxisFix(src);
		var err = ResourceSaver.Save(src, fullPath);
		if (err != Error.Ok)
			GD.PushWarning($"[MergedAnimationPlayer] ResourceSaver.Save({fullPath}) → {err}");
		string finalName = System.IO.Path.GetFileNameWithoutExtension(file);
		if (lib.HasAnimation(finalName))
			lib.RemoveAnimation(finalName);
		lib.AddAnimation(finalName, src);
	}

	private void ProcessSceneFile(string file, string fullPath, string subFolder, AnimationLibrary lib)
	{
		var packed = ResourceLoader.Load<PackedScene>(fullPath);
		if (packed == null)
		{
			GD.PushWarning($"[MergedAnimationPlayer] Skipped '{fullPath}' — load returned null");
			return;
		}
		var inst = packed.Instantiate<Node>();
		var srcPlayer = FindFirstOfType<AnimationPlayer>(inst);
		if (srcPlayer != null)
		{
			foreach (StringName srcName in srcPlayer.GetAnimationList())
			{
				var src = srcPlayer.GetAnimation(srcName);
				if (src == null)
					continue;
				var clone = (Animation)src.Duplicate();
				RetargetTrackPaths(clone, _skeletonRelative);
				ApplyAxisFix(clone);
				string finalName = ComposeAnimName(file, srcName);

				string tresPath = $"{subFolder.TrimEnd('/')}/{finalName}.tres";
				clone.ResourcePath = tresPath;
				var err = ResourceSaver.Save(clone, tresPath);
				Animation animToStore = clone;
				if (err == Error.Ok)
				{
					var reloaded = ResourceLoader.Load<Animation>(tresPath, "", ResourceLoader.CacheMode.Replace);
					if (reloaded != null)
						animToStore = reloaded;
				}
				else
				{
					GD.PushWarning($"[MergedAnimationPlayer] ResourceSaver.Save({tresPath}) → {err}");
				}

				if (lib.HasAnimation(finalName))
					lib.RemoveAnimation(finalName);
				lib.AddAnimation(finalName, animToStore);
			}
		}
		inst.QueueFree();
	}

	private void StepEmit()
	{
		int total = 0;
		var savedTo = new List<string>();
		foreach (var (libName, lib) in _pendingLibs)
		{
			string libPath = $"{AnimationsFolder.TrimEnd('/')}/{libName}/{libName}.res";
			lib.ResourcePath = libPath;
			var saveErr = ResourceSaver.Save(lib, libPath);
			AnimationLibrary libToAttach = lib;
			if (saveErr == Error.Ok)
			{
				var reloaded = ResourceLoader.Load<AnimationLibrary>(libPath, "", ResourceLoader.CacheMode.Replace);
				if (reloaded != null)
					libToAttach = reloaded;
				savedTo.Add(libPath);
			}
			else
			{
				GD.PushWarning($"[MergedAnimationPlayer] ResourceSaver.Save({libPath}) → {saveErr}");
			}

			if (HasAnimationLibrary(libName))
				RemoveAnimationLibrary(libName);
			AddAnimationLibrary(libName, libToAttach);
			total += libToAttach.GetAnimationList().Count;
		}

		Status = $"DONE {_pendingLibs.Count} library(s), {total} animation(s) (was {_existingAnimCount}) from {_filesToProcess.Count} file(s)";
		_statusDoneTimer = 3f;
		_phase = Phase.Idle;
		_filesToProcess = null;
		_pendingLibs = null;
		_skeleton = null;
	}

	private void Stop(string reason)
	{
		if (_phase == Phase.Idle)
			return;
		Status = reason;
		_statusDoneTimer = 3f;
		_phase = Phase.Idle;
		_filesToProcess = null;
		_pendingLibs = null;
		_skeleton = null;
	}

	private void ApplyAxisFix(Animation anim)
	{
		if (RootRotationOffsetDegrees == Vector3.Zero)
			return;
		if (_skeleton == null)
		{
			GD.PushWarning("[MergedAnimationPlayer] _skeleton is null in ApplyAxisFix — fix skipped");
			return;
		}

		Quaternion offset = Quaternion.FromEuler(new Vector3(
			Mathf.DegToRad(RootRotationOffsetDegrees.X),
			Mathf.DegToRad(RootRotationOffsetDegrees.Y),
			Mathf.DegToRad(RootRotationOffsetDegrees.Z)));

		var animBoneNames = new HashSet<string>();
		for (int t = 0; t < anim.GetTrackCount(); t++)
		{
			var tt = anim.TrackGetType(t);
			if (tt != Animation.TrackType.Rotation3D && tt != Animation.TrackType.Position3D)
				continue;
			string sub = anim.TrackGetPath(t).GetConcatenatedSubNames().ToString();
			if (!string.IsNullOrEmpty(sub))
				animBoneNames.Add(sub);
		}

		// "Animation roots" = bones whose parent in the target skeleton is NOT itself animated.
		// Applying the offset there propagates correctly through parent transform composition.
		var targetBones = new HashSet<string>();
		foreach (string boneName in animBoneNames)
		{
			int boneIdx = _skeleton.FindBone(boneName);
			if (boneIdx < 0)
				continue;
			int parentIdx = _skeleton.GetBoneParent(boneIdx);
			string parentName = parentIdx >= 0 ? _skeleton.GetBoneName(parentIdx) : "";
			if (!animBoneNames.Contains(parentName))
				targetBones.Add(boneName);
		}

		if (!_axisFixLogged)
		{
			GD.Print($"[MergedAnimationPlayer] AxisFix offset={RootRotationOffsetDegrees}, roots={targetBones.Count}: [{string.Join(", ", targetBones)}]");
			_axisFixLogged = true;
		}

		for (int t = 0; t < anim.GetTrackCount(); t++)
		{
			var type = anim.TrackGetType(t);
			if (type != Animation.TrackType.Rotation3D && type != Animation.TrackType.Position3D)
				continue;
			string sub = anim.TrackGetPath(t).GetConcatenatedSubNames().ToString();
			if (!targetBones.Contains(sub))
				continue;

			int keys = anim.TrackGetKeyCount(t);
			for (int k = keys - 1; k >= 0; k--)
			{
				double time = anim.TrackGetKeyTime(t, k);
				if (type == Animation.TrackType.Rotation3D)
				{
					Quaternion q = (Quaternion)anim.TrackGetKeyValue(t, k);
					anim.TrackRemoveKey(t, k);
					anim.RotationTrackInsertKey(t, time, offset * q);
				}
				else
				{
					Vector3 pos = (Vector3)anim.TrackGetKeyValue(t, k);
					anim.TrackRemoveKey(t, k);
					anim.PositionTrackInsertKey(t, time, offset * pos);
				}
			}
		}
	}

	private static void RetargetTrackPaths(Animation anim, NodePath skeletonRelative)
	{
		for (int t = 0; t < anim.GetTrackCount(); t++)
		{
			NodePath oldPath = anim.TrackGetPath(t);
			string boneSub = oldPath.GetConcatenatedSubNames().ToString();
			if (string.IsNullOrEmpty(boneSub))
				continue;
			anim.TrackSetPath(t, $"{skeletonRelative}:{boneSub}");
		}
	}

	private static bool IsAnimationFile(string file)
	{
		return file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
			|| file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
			|| file.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)
			|| file.EndsWith(".tres", StringComparison.OrdinalIgnoreCase);
	}

	private static string ComposeAnimName(string fileName, StringName srcAnimName)
	{
		string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
		string srcStr = srcAnimName.ToString();
		if (string.IsNullOrEmpty(srcStr) || srcStr == "default" || srcStr == "mixamo.com" || srcStr == "Take 001")
			return baseName;
		return $"{baseName}_{srcStr}";
	}

	private static T FindFirstOfType<T>(Node node) where T : Node
	{
		foreach (var c in node.GetChildren())
		{
			if (c is T match)
				return match;
			var nested = FindFirstOfType<T>(c);
			if (nested != null)
				return nested;
		}
		return null;
	}

	private void Fail(string reason)
	{
		Status = $"FAIL: {reason}";
		_statusDoneTimer = 4f;
		_phase = Phase.Idle;
		GD.PushWarning($"[MergedAnimationPlayer] {reason}");
	}
}
