namespace Vantix.Editor;

#if TOOLS
using Godot;

/// <summary>
/// Editor plugin entry point: registers a wireframe-box gizmo for <see cref="Zone"/> and a second
/// for <see cref="BombSpot"/>. Uses Godot's own <see cref="EditorNode3DGizmoPlugin"/> system so
/// the standard 3D View → Gizmos toggle hides / shows these outlines automatically (same as the
/// built-in CollisionShape3D outline). The Zone / BombSpot nodes themselves don't render any
/// editor-visible geometry — that's the plugin's job.
/// </summary>
[Tool]
public partial class SpotsGizmoPlugin : EditorPlugin
{
	private ZoneGizmoPlugin _zoneGizmo;
	private BombSpotGizmoPlugin _bombSpotGizmo;
	private SpawnGizmoPlugin _spawnGizmo;

	public override void _EnterTree()
	{
		_zoneGizmo = new ZoneGizmoPlugin();
		_bombSpotGizmo = new BombSpotGizmoPlugin();
		_spawnGizmo = new SpawnGizmoPlugin();
		AddNode3DGizmoPlugin(_zoneGizmo);
		AddNode3DGizmoPlugin(_bombSpotGizmo);
		AddNode3DGizmoPlugin(_spawnGizmo);
	}

	public override void _ExitTree()
	{
		if (_zoneGizmo != null) RemoveNode3DGizmoPlugin(_zoneGizmo);
		if (_bombSpotGizmo != null) RemoveNode3DGizmoPlugin(_bombSpotGizmo);
		if (_spawnGizmo != null) RemoveNode3DGizmoPlugin(_spawnGizmo);
		_zoneGizmo = null;
		_bombSpotGizmo = null;
		_spawnGizmo = null;
	}
}
#endif
