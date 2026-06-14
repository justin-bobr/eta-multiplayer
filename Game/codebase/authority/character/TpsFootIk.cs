using Godot;

/// <summary>
/// TPS Foot IK: Godot <c>TwoBoneIK3D</c> per leg plus a ground-snap raycast. Ground adaptation only, no
/// procedural stepping — foot targets are snapped each frame to the ground under the hip, so animated legs
/// adapt to slopes/stairs. Requires a TwoBoneIK3D (thigh/calf/foot bones), a foot-target Marker3D and a
/// pole Marker3D per leg, wired into this node's [Export] fields.
/// </summary>
public class TpsFootIk
{
	public Skeleton3D Skeleton;
	public uint GroundMask = 1;
	public float FootHipWidth = 0.13f;
	public float GroundProbeUp = 0.8f;
	public float GroundProbeDown = 2.0f;
	public float FootGroundOffset = 0.10f;
	public Rid PlayerRid;
	public bool EnableLegIK = true;
	public bool EnableDebugMarkers = false;
	public float IkInfluenceLerpRate = 10f;
	public float KneeForwardOffset = 0.6f;
	public float KneeDownOffset = 0.2f;

	public Node IkLeftNode, IkRightNode;
	public Node3D TargetLeft, TargetRight;
	public Node3D PoleLeft, PoleRight;

	private int _thighL = -1, _thighR = -1;
	private float _ikInfluenceL, _ikInfluenceR;

	private MeshInstance3D _dbgFootLMarker, _dbgFootRMarker;
	private MeshInstance3D _dbgRayMesh;
	private readonly System.Collections.Generic.List<(Vector3 from, Vector3 to, bool hit)> _dbgRays = new();

	/// <summary>Resolves the thigh bones and validates that the wired TwoBoneIK3D refs are present.
	/// Returns false (and disables IK) if anything is missing.</summary>
	public bool Initialize(Skeleton3D skeleton)
	{
		Skeleton = skeleton;
		if (skeleton == null) return false;

		_thighL = skeleton.FindBone("thigh_l");
		_thighR = skeleton.FindBone("thigh_r");

		if (_thighL < 0 || _thighR < 0)
		{
			GD.PushWarning("[TpsFootIk] thigh_l/thigh_r not found — IK disabled.");
			return false;
		}

		if (IkLeftNode == null || IkRightNode == null || TargetLeft == null || TargetRight == null)
		{
			GD.PushWarning("[TpsFootIk] TwoBoneIK3D / target markers not wired in NetworkPlayer inspector. " +
						   "Foot IK disabled — setup instructions in TpsFootIk class doc.");
			return false;
		}

		return true;
	}

	/// <summary>Per-tick update: snaps both foot targets to the ground beneath the hips, positions
	/// the knee pole markers, and smooth-lerps the IK influence.</summary>
	public void Update(float dt, Vector3 horizVelocity, Vector3 charWorldPos, Basis charWorldBasis, PhysicsDirectSpaceState3D space)
	{
		if (Skeleton == null || _thighL < 0) return;

		Vector3 hipL_world = Skeleton.GlobalTransform * Skeleton.GetBoneGlobalPose(_thighL).Origin;
		Vector3 hipR_world = Skeleton.GlobalTransform * Skeleton.GetBoneGlobalPose(_thighR).Origin;

		Vector3 footL = new Vector3(hipL_world.X, charWorldPos.Y, hipL_world.Z);
		Vector3 footR = new Vector3(hipR_world.X, charWorldPos.Y, hipR_world.Z);
		GroundSnap(ref footL, space);
		GroundSnap(ref footR, space);

		if (TargetLeft != null) TargetLeft.GlobalPosition = footL;
		if (TargetRight != null) TargetRight.GlobalPosition = footR;

		Vector3 facing = -charWorldBasis.Z; facing.Y = 0;
		facing = facing.LengthSquared() > 0.001f ? facing.Normalized() : Vector3.Forward;
		if (PoleLeft != null) PoleLeft.GlobalPosition = hipL_world + facing * KneeForwardOffset + Vector3.Down * KneeDownOffset;
		if (PoleRight != null) PoleRight.GlobalPosition = hipR_world + facing * KneeForwardOffset + Vector3.Down * KneeDownOffset;

		float targetInfluence = EnableLegIK ? 1f : 0f;
		_ikInfluenceL = Mathf.Lerp(_ikInfluenceL, targetInfluence, 1f - Mathf.Exp(-IkInfluenceLerpRate * dt));
		_ikInfluenceR = Mathf.Lerp(_ikInfluenceR, targetInfluence, 1f - Mathf.Exp(-IkInfluenceLerpRate * dt));
		SetIkProperty(IkLeftNode, _ikInfluenceL);
		SetIkProperty(IkRightNode, _ikInfluenceR);

		if (EnableDebugMarkers)
		{
			UpdateDebugMarkers(footL, footR);
			UpdateDebugRays();
		}
		else
		{
			_dbgRays.Clear();
		}
	}

	/// <summary>Pushes the active/influence properties to a TwoBoneIK3D node via dynamic Set.</summary>
	private static void SetIkProperty(Node ikNode, float influence)
	{
		if (ikNode == null) return;
		ikNode.Set("active", influence > 0.01f);
		ikNode.Set("influence", influence);
	}

	private PhysicsRayQueryParameters3D _snapQuery;
	private readonly PhysicsRayQueryResult3D _snapResult = new();
	private readonly Godot.Collections.Array<Rid> _snapExclude = new();

	/// <summary>Raycasts down and snaps the world position to the hit, adding FootGroundOffset
	/// to compensate for the foot bone height above the mesh footprint.</summary>
	private void GroundSnap(ref Vector3 worldPos, PhysicsDirectSpaceState3D space)
	{
		if (space == null) return;
		Vector3 from = worldPos + Vector3.Up * GroundProbeUp;
		Vector3 to = worldPos - Vector3.Up * GroundProbeDown;
		if (_snapQuery == null)
		{
			_snapQuery = PhysicsRayQueryParameters3D.Create(from, to, GroundMask);
			_snapQuery.Exclude = _snapExclude;
		}
		_snapExclude.Clear();
		if (PlayerRid.IsValid) _snapExclude.Add(PlayerRid);
		_snapQuery.From = from;
		_snapQuery.To = to;
		_snapQuery.CollisionMask = GroundMask;
		bool didHit = space.IntersectRayInto(_snapQuery, _snapResult);
		Vector3 endPoint = didHit ? _snapResult.GetPosition() : to;
		if (didHit)
			worldPos.Y = endPoint.Y + FootGroundOffset;
		if (EnableDebugMarkers)
			_dbgRays.Add((from, endPoint, didHit));
	}

	/// <summary>Spawns/updates the debug sphere markers at the snapped foot positions.</summary>
	private void UpdateDebugMarkers(Vector3 footL, Vector3 footR)
	{
		var parent = Skeleton.GetTree()?.CurrentScene;
		if (parent == null) return;
		EnsureMarker(ref _dbgFootLMarker, parent, new Color(1, 0.2f, 0.2f), 0.05f);
		EnsureMarker(ref _dbgFootRMarker, parent, new Color(1, 0.5f, 0.2f), 0.05f);
		_dbgFootLMarker.GlobalPosition = footL;
		_dbgFootRMarker.GlobalPosition = footR;
	}

	/// <summary>Rebuilds the immediate-mesh debug lines for the collected ground-probe rays this frame.</summary>
	private void UpdateDebugRays()
	{
		var parent = Skeleton.GetTree()?.CurrentScene;
		if (parent == null) { _dbgRays.Clear(); return; }

		if (_dbgRayMesh == null || !Godot.GodotObject.IsInstanceValid(_dbgRayMesh))
		{
			_dbgRayMesh = new MeshInstance3D
			{
				Mesh = new ImmediateMesh(),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				CustomAabb = new Aabb(new Vector3(-100, -100, -100), new Vector3(200, 200, 200)),
				MaterialOverride = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					VertexColorUseAsAlbedo = true,
					NoDepthTest = true,
				},
			};
			parent.AddChild(_dbgRayMesh);
		}

		var mesh = (ImmediateMesh)_dbgRayMesh.Mesh;
		mesh.ClearSurfaces();
		if (_dbgRays.Count > 0)
		{
			mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
			foreach (var (from, to, hit) in _dbgRays)
			{
				Color c = hit ? new Color(0.2f, 1f, 0.2f) : new Color(1f, 0.2f, 0.2f);
				mesh.SurfaceSetColor(c);
				mesh.SurfaceAddVertex(from);
				mesh.SurfaceSetColor(c);
				mesh.SurfaceAddVertex(to);
			}
			mesh.SurfaceEnd();
		}
		_dbgRays.Clear();
	}

	/// <summary>Lazily creates an unshaded debug sphere marker as a child of the given parent.</summary>
	private static void EnsureMarker(ref MeshInstance3D marker, Node parent, Color color, float radius)
	{
		if (marker != null && Godot.GodotObject.IsInstanceValid(marker)) return;
		marker = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 12, Rings = 6 },
			MaterialOverride = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = color,
				NoDepthTest = true,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(marker);
	}
}
