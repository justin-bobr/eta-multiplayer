namespace Vantix.Editor;

#if TOOLS
using Godot;

/// <summary>Draws a wireframe outline of <see cref="Zone.Size"/> for every <see cref="Zone"/>
/// node in the edited scene. Registered by <see cref="SpotsGizmoPlugin"/> at editor startup.
/// Gizmo visibility follows the 3D View → Gizmos toggle automatically (reason for using
/// <see cref="EditorNode3DGizmoPlugin"/> over a child MeshInstance3D). Redraw is triggered by
/// Zone.Size setter via <see cref="Node3D.UpdateGizmos"/>.</summary>
[Tool]
public partial class ZoneGizmoPlugin : EditorNode3DGizmoPlugin
{
	private const string OutlineMat = "zone_outline";
	private const string FillMat = "zone_fill";

	public ZoneGizmoPlugin()
	{
		// Cyan — distinguishes Zone outlines from BombSpot's red.
		CreateMaterial(OutlineMat, new Color(0.30f, 0.85f, 1.00f));
		// Semi-transparent fill. Unshaded + alpha + no-cull so it reads from any angle.
		var fill = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.30f, 0.85f, 1.00f, 0.12f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		AddMaterial(FillMat, fill);
	}

	public override string _GetGizmoName() => "Zone";
	// Plain Zone only. BombSpot and Spawn extend Zone and have their own plugins; without the
	// exclusion they'd be drawn twice.
	public override bool _HasGizmo(Node3D node) => node is Zone && node is not BombSpot && node is not Spawn;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();
		if (gizmo.GetNode3D() is not Zone z) return;
		gizmo.AddLines(GizmoBoxBuilder.BuildLines(z.Size), GetMaterial(OutlineMat, gizmo));
		gizmo.AddMesh(GizmoBoxBuilder.BuildBoxMesh(z.Size), GetMaterial(FillMat, gizmo));
	}
}
#endif
