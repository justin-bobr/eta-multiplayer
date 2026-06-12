using Godot;

/// <summary>
/// Root script of world.tscn. Owns the single <see cref="global::Level"/> instance for the active map
/// (the instanced map root carries the <see cref="global::Level"/> script) and exposes it statically so
/// every gameplay system can reach the map's authored markers — the HUD (zone / bombsite lookups), the
/// bot AI (nav targets), the spawn system, and the preview-camera cycler.
///
/// Replaces the old static scan cache: there is exactly one active world at a time, so a static
/// <see cref="Instance"/> mirrors that. The <see cref="LevelPath"/> export points at the instanced map
/// node; if left unset, <see cref="ResolveLevel"/> falls back to the first <see cref="global::Level"/>
/// descendant so the wiring still works when a map is swapped under the World root.
/// </summary>
[GlobalClass]
public partial class World : Node3D
{
	/// <summary>The live World root, or null between scene switches.</summary>
	public static World Instance { get; private set; }

	/// <summary>The active map's <see cref="global::Level"/> registry, or null before the world is ready.
	/// Resolves (and <see cref="global::Level.EnsureResolved"/>s) lazily on first access.</summary>
	public static Level Level => Instance?.ResolveLevel();

	/// <summary>Path to the instanced map root — the node carrying the <see cref="global::Level"/> script.
	/// Leave unset to auto-discover the first <see cref="global::Level"/> descendant.</summary>
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
