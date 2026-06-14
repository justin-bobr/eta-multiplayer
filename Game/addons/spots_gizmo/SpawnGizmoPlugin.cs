#if TOOLS
using Godot;

/// <summary>Draws a wireframe outline of <see cref="Spawn.Size"/> for every <see cref="Spawn"/>
/// node. Registered by <see cref="SpotsGizmoPlugin"/>. Green so spawn regions read distinctly
/// from BombSpot (red) and Zone (cyan) in the editor viewport.</summary>
[Tool]
public partial class SpawnGizmoPlugin : EditorNode3DGizmoPlugin
{
	private const string OutlineMat = "spawn_outline";
	private const string FillMat = "spawn_fill";

	public SpawnGizmoPlugin()
	{
		CreateMaterial(OutlineMat, new Color(0.30f, 0.95f, 0.40f));
		var fill = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.30f, 0.95f, 0.40f, 0.12f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		AddMaterial(FillMat, fill);
	}

	public override string _GetGizmoName() => "Spawn";
	public override bool _HasGizmo(Node3D node) => node is Spawn;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();
		if (gizmo.GetNode3D() is not Spawn s) return;
		gizmo.AddLines(GizmoBoxBuilder.BuildLines(s.Size), GetMaterial(OutlineMat, gizmo));
		gizmo.AddMesh(GizmoBoxBuilder.BuildBoxMesh(s.Size), GetMaterial(FillMat, gizmo));
	}
}
#endif
