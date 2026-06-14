using Godot;

/// <summary>Root script of world.tscn. Owns the active map's single <see cref="global::Level"/> registry and
/// exposes it statically (via <see cref="Instance"/>) to the HUD, bot AI, spawn system, and preview-camera
/// cycler. <see cref="LevelPath"/> points at the instanced map node; if unset, <see cref="ResolveLevel"/> falls
/// back to the first <see cref="global::Level"/> descendant.</summary>
[GlobalClass]
public partial class World : Node3D
{
	/// <summary>The live World root, or null between scene switches.</summary>
	public static World Instance { get; private set; }

	/// <summary>The active map's <see cref="global::Level"/> registry (lazily resolved), or null before the world is ready.</summary>
	public static Level Level => Instance?.ResolveLevel();

	/// <summary>Path to the instanced map root (the node carrying the <see cref="global::Level"/> script). Unset = auto-discover.</summary>
	[Export] public NodePath LevelPath { get; set; }

	private Level _level;

	public override void _EnterTree() => Instance = this;

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		_level = null;
	}

	private Level ResolveLevel()
	{
		if (_level != null && GodotObject.IsInstanceValid(_level))
		{
			_level.EnsureResolved();
			return _level;
		}
		_level = (LevelPath != null && !LevelPath.IsEmpty) ? GetNodeOrNull<Level>(LevelPath) : null;
		_level ??= FindFirstLevel(this);
		if (_level == null)
		{
			GD.PushWarning("[World] No Level node found — set LevelPath on the World root to the map instance.");
			return null;
		}
		_level.EnsureResolved();
		return _level;
	}

	private static Level FindFirstLevel(Node node)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is Level l) return l;
			var nested = FindFirstLevel(child);
			if (nested != null) return nested;
		}
		return null;
	}
}
