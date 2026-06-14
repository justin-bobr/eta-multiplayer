using Godot;

namespace Vantix.Debug;

/// <summary>
/// Red transparent capsule at the server-reported local player position (from <see cref="NetClient.LastSelfSnap"/>),
/// for spotting drift between server-authority and predicted client body. Height derives from CrouchBlend
/// per <see cref="NetworkPlayer.ApplyCrouchHeight"/>. Toggle via <c>sv_debug_capsule 1</c>.
/// </summary>
public partial class ServerBodyCapsuleDebug : Node3D
{
	private MeshInstance3D _capsuleNode;
	private CapsuleMesh _capsuleMesh;

	public override void _Ready()
	{
		TopLevel = true;
		_capsuleMesh = new CapsuleMesh
		{
			Radius = 0.4f,
			Height = 1.8f,
			RadialSegments = 8,
			Rings = 4,
		};
		_capsuleNode = new MeshInstance3D
		{
			Mesh = _capsuleMesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.15f, 0.15f, 0.30f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		AddChild(_capsuleNode);
	}

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("ServerBodyCapsuleDebug._Process");
		if (!ConVars.Sv.DebugCapsule) { _capsuleNode.Visible = false; return; }
		var client = NetMain.Instance?.Client;
		if (client?.LastSelfSnap == null) { _capsuleNode.Visible = false; return; }
		var local = NetMain.Instance?.FindLocalPlayer();
		if (local == null) { _capsuleNode.Visible = false; return; }

		var snap = client.LastSelfSnap.Value;
		float crouchBlend = snap.CrouchBlend / 255f;
		float h = Mathf.Lerp(local.StandHeight, local.CrouchHeight, crouchBlend);
		if (!Mathf.IsEqualApprox(_capsuleMesh.Height, h)) _capsuleMesh.Height = h;
		if (!Mathf.IsEqualApprox(_capsuleMesh.Radius, local.CapsuleRadius)) _capsuleMesh.Radius = local.CapsuleRadius;
		_capsuleNode.GlobalPosition = snap.Pos + new Vector3(0f, h * 0.5f, 0f);
		_capsuleNode.Visible = true;
	}
}
