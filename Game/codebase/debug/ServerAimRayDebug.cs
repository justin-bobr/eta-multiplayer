using Godot;

/// <summary>
/// Draws a yellow ray (ImmediateMesh) from the server eye in the server's believed aim direction,
/// from <see cref="NetClient.LastSelfSnap"/> (eye = Snap.Pos + StandEyeHeight). Active only when
/// <c>sv_debug_aim_ray</c> is set.
/// </summary>
public partial class ServerAimRayDebug : Node3D
{
	private const float RayLength = 30f;
	private const float StandEyeHeight = 1.7f;

	private MeshInstance3D _meshInstance;
	private ImmediateMesh _mesh;

	public override void _Ready()
	{
		TopLevel = true;
		_mesh = new ImmediateMesh();
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.9f, 0.2f, 0.9f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		_meshInstance = new MeshInstance3D
		{
			Mesh = _mesh,
			MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_meshInstance);
	}

	private double _logAccum;

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("ServerAimRayDebug._Process");
		_mesh.ClearSurfaces();
		if (!ConVars.Sv.DebugAimRay) return;
		var client = NetMain.Instance?.Client;
		if (client?.LastSelfSnap == null) return;

		var snap = client.LastSelfSnap.Value;
		Vector3 serverEye = snap.Pos + Vector3.Up * StandEyeHeight;

		float aimPunchDegX = snap.AimPunchX / 16f;
		float aimPunchDegY = snap.AimPunchY / 16f;
		float effYaw = snap.Yaw + Mathf.DegToRad(aimPunchDegY);
		float effPitch = snap.Pitch - Mathf.DegToRad(aimPunchDegX);
		var basis = new Basis(Vector3.Up, effYaw) * new Basis(Vector3.Right, effPitch);
		Vector3 serverForward = -basis.Z;
		Vector3 serverAimEndpoint = serverEye + serverForward * RayLength;

		var localPlayer = NetMain.Instance?.FindLocalPlayer();
		Vector3 origin = localPlayer?.ActiveCamera != null
			? localPlayer.ActiveCamera.GlobalPosition
			: serverEye;

		_mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
		_mesh.SurfaceAddVertex(origin);
		_mesh.SurfaceAddVertex(serverAimEndpoint);
		_mesh.SurfaceEnd();

		_logAccum += delta;
		if (_logAccum >= 1.0)
		{
			_logAccum = 0;
			float localPitchDeg = localPlayer?.HeadPitch != null ? Mathf.RadToDeg(localPlayer.HeadPitch.Rotation.X) : 0f;
			float localYawDeg = localPlayer != null ? Mathf.RadToDeg(localPlayer.Rotation.Y) : 0f;
			float localAimPunchX = localPlayer?.Movement.AimPunch.X ?? 0f;
			float localAimPunchY = localPlayer?.Movement.AimPunch.Y ?? 0f;
			Dbg.Print($"[sv-aim] camOrigin=({origin.X:F2},{origin.Y:F2},{origin.Z:F2}) serverEye=({serverEye.X:F2},{serverEye.Y:F2},{serverEye.Z:F2}) | snap yaw={Mathf.RadToDeg(snap.Yaw):F1}° pitch={Mathf.RadToDeg(snap.Pitch):F1}° aimPunch=({aimPunchDegX:F2}°,{aimPunchDegY:F2}°) | local yaw={localYawDeg:F1}° pitch={localPitchDeg:F1}° aimPunch=({localAimPunchX:F2}°,{localAimPunchY:F2}°)");
		}
	}
}
