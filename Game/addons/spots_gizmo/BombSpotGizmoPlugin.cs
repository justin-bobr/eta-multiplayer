namespace Vantix.Editor;

#if TOOLS
using Godot;

/// <summary>Draws a wireframe outline of <see cref="BombSpot.Size"/> for every <see cref="BombSpot"/>
/// node in the edited scene. Registered by <see cref="SpotsGizmoPlugin"/> at editor startup.
/// Gizmo visibility follows the 3D View → Gizmos toggle automatically. Redraw is triggered by
/// BombSpot.Size setter via <see cref="Node3D.UpdateGizmos"/>.</summary>
[Tool]
public partial class BombSpotGizmoPlugin : EditorNode3DGizmoPlugin
{
	private const string OutlineMat = "bombspot_outline";
	private const string FillMat = "bombspot_fill";

	public BombSpotGizmoPlugin()
	{
		// Red — distinguishes plant regions from Zones (cyan).
		CreateMaterial(OutlineMat, new Color(1.00f, 0.30f, 0.25f));
		// Semi-transparent fill. Unshaded + alpha + no-cull so it reads from any angle.
		var fill = new StandardMaterial3D
		{
			AlbedoColor = new Color(1.00f, 0.30f, 0.25f, 0.14f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		AddMaterial(FillMat, fill);
	}

	public override string _GetGizmoName() => "BombSpot";
	public override bool _HasGizmo(Node3D node) => node is BombSpot;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();
		if (gizmo.GetNode3D() is not BombSpot bs) return;
		gizmo.AddLines(GizmoBoxBuilder.BuildLines(bs.Size), GetMaterial(OutlineMat, gizmo));
		gizmo.AddMesh(GizmoBoxBuilder.BuildBoxMesh(bs.Size), GetMaterial(FillMat, gizmo));
	}
}
#endif
