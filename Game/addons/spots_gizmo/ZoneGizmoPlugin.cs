#if TOOLS
using Godot;

/// <summary>Draws a wireframe outline of <see cref="Zone.Size"/> for every <see cref="Zone"/>
/// node in the edited scene. Registered by <see cref="SpotsGizmoPlugin"/> at editor startup.
/// Gizmo visibility follows the 3D View → Gizmos toggle automatically — this is the whole point
/// of using <see cref="EditorNode3DGizmoPlugin"/> instead of a child MeshInstance3D. Redraw is
/// triggered by Zone.Size setter via <see cref="Node3D.UpdateGizmos"/>.</summary>
[Tool]
public partial class ZoneGizmoPlugin : EditorNode3DGizmoPlugin
{
	private const string OutlineMat = "zone_outline";
	private const string FillMat = "zone_fill";

	public ZoneGizmoPlugin()
	{
		// Cyan, visually distinguishes Zone outlines from BombSpot's red ones in the viewport.
		CreateMaterial(OutlineMat, new Color(0.30f, 0.85f, 1.00f));
		// Soft semi-transparent fill so the box has volume in the viewport but doesn't obscure
		// what's inside it. Set unshaded + alpha + no-depth so it reads consistently from any
		// angle and through other geometry.
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
	// Match plain Zone only — BombSpot and Spawn extend Zone and have their own plugins with
	// distinct colours. Without the exclusion they'd get drawn twice (cyan from this plugin
	// AND red / green from their specialised one).
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
