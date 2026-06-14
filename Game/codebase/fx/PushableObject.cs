using Godot;

namespace Vantix.Fx;

/// <summary>
/// Pushable <see cref="RigidBody3D"/> (e.g. a car). The player holds the push action and presses
/// against it; after <see cref="MinChargeSeconds"/> of sustained contact aligned with
/// <see cref="PushDirection"/>, force is applied each physics tick. A 2D prompt shows when the
/// player is close behind. Editor [Tool] mode draws a yellow direction arrow (not saved). Client-local only.
/// </summary>
[Tool]
public partial class PushableObject : RigidBody3D
{
	[Export] public Vector3 PushDirection = new(0f, 0f, -1f);
	[Export] public float PushForce = 2500f;
	[Export] public float MinChargeSeconds = 1.5f;
	[Export] public float ChargeDecayRate = 2f;
	[Export] public float MaxPushDistance = 4.0f;
	[Export] public StringName PushAction = "action";
	[Export(PropertyHint.Range, "0,1,0.05")] public float AlignThreshold = 0.5f;

	[ExportGroup("Prompt (2D hint)")]
	[Export] public float PromptRange = 3.5f;
	[Export] public string PromptText = "Schieben";
	[Export] public int PromptFontSize = 26;

	private NetworkPlayer _player;
	private float _charge;
	private Vector3 _startPos;
	private bool _hasStart;
	private bool _wasMoving;
	private double _nextDiagAt;

	private CanvasLayer _promptLayer;
	private Label _promptLabel;
	private MeshInstance3D _dirGizmo;

	/// <summary>Caches the spawn position for the max-push-distance limit; builds the editor gizmo on scene open.</summary>
	public override void _Ready()
	{
		if (Engine.IsEditorHint()) { UpdateDirectionGizmo(); return; }

		_startPos = GlobalPosition;
		_hasStart = true;
	}

	/// <summary>Editor-only: keeps the direction arrow in sync with the live PushDirection value.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("PushableObject._Process");
		if (Engine.IsEditorHint()) UpdateDirectionGizmo();
	}

	private const float ActiveDistanceSq = 5f * 5f;
	private int _wakeRecheckTickCounter;

	/// <summary>Runs the per-tick push logic: detects contact, builds charge, applies force when ready.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("PushableObject._PhysicsProcess");
		if (Engine.IsEditorHint()) return;

		_wakeRecheckTickCounter++;
		if (_charge <= 0f && !_wasMoving && Sleeping && (_wakeRecheckTickCounter & 7) != 0) return;
		var localPlayer = NetMain.Instance?.LocalPlayer;
		if (localPlayer != null && _charge <= 0f && !_wasMoving)
		{
			float distSq = (localPlayer.GlobalPosition - GlobalPosition).LengthSquared();
			if (distSq > ActiveDistanceSq) return;
		}

		float dt = (float)delta;
		Vector3 pushWorld = (GlobalTransform.Basis * PushDirection).Normalized();
		if (pushWorld.LengthSquared() < 0.0001f) return;

		NetworkPlayer player = ResolvePlayer();
		float traveled = _hasStart ? (GlobalPosition - _startPos).Length() : 0f;
		bool atLimit = MaxPushDistance > 0f && traveled >= MaxPushDistance;

		UpdatePrompt(player, pushWorld, atLimit);

		bool actionHeld = Input.IsActionPressed(PushAction);
		bool pushing = IsBeingPushed(player, pushWorld);
		_charge = pushing ? _charge + dt : Mathf.Max(0f, _charge - dt * ChargeDecayRate);

		double now = Time.GetTicksMsec() / 1000.0;
		if (actionHeld && now >= _nextDiagAt)
		{
			_nextDiagAt = now + 0.5;
			string contactInfo = player != null
				? DescribeContacts(player, pushWorld)
				: "no-player";
			Dbg.Print($"[push:diag] {Name} action={actionHeld} pushing={pushing} charge={_charge:F2}/{MinChargeSeconds:F2} atLimit={atLimit} sleeping={Sleeping} | {contactInfo}");
		}

		if (pushing && _charge >= MinChargeSeconds && !atLimit)
		{
			if (Sleeping) Sleeping = false;
			ApplyCentralForce(pushWorld * PushForce);
			if (!_wasMoving)
				Dbg.Print($"[push] {Name} starts moving ({traveled:F2}/{MaxPushDistance:F1} m, force={PushForce:F0}N, mass={Mass:F0}kg)");
			_wasMoving = true;
		}
		else
		{
			if (_wasMoving) Dbg.Print($"[push] {Name} stops (force off)");
			_wasMoving = false;
		}
	}

	/// <summary>Diagnostic helper: lists each slide collision of the player along with its dot to PushDirection.</summary>
	private string DescribeContacts(NetworkPlayer p, Vector3 pushWorld)
	{
		int n = p.GetSlideCollisionCount();
		if (n == 0) return "no slide-contacts (player not pressing against anything)";
		var sb = new System.Text.StringBuilder();
		sb.Append($"{n} contact(s): ");
		for (int i = 0; i < n; i++)
		{
			KinematicCollision3D col = p.GetSlideCollision(i);
			GodotObject obj = col.GetCollider();
			string name = obj is Node node ? node.Name.ToString() : obj?.GetType().Name ?? "null";
			bool isUs = obj is Node nn && (nn == this || IsAncestorOf(nn));
			float dot = (-col.GetNormal()).Dot(pushWorld);
			sb.Append($"[{name}{(isUs ? "*" : "")} dot={dot:F2}] ");
		}
		return sb.ToString();
	}

	/// <summary>True when the player holds the push action and presses against this body along PushDirection.</summary>
	private bool IsBeingPushed(NetworkPlayer p, Vector3 pushWorld)
	{
		if (p == null || !Input.IsActionPressed(PushAction)) return false;

		for (int i = 0; i < p.GetSlideCollisionCount(); i++)
		{
			KinematicCollision3D col = p.GetSlideCollision(i);
			if (col.GetCollider() is not Node hit) continue;
			if (hit != this && !IsAncestorOf(hit)) continue;
			if ((-col.GetNormal()).Dot(pushWorld) >= AlignThreshold) return true;
		}
		return false;
	}

	/// <summary>Shows the 2D hint when the player is close enough behind the object.</summary>
	private void UpdatePrompt(NetworkPlayer p, Vector3 pushWorld, bool atLimit)
	{
		bool near = false;
		if (p != null && !atLimit)
		{
			Vector3 toPlayer = p.GlobalPosition - GlobalPosition;
			bool behind = toPlayer.Dot(pushWorld) < 0f;
			near = behind && toPlayer.Length() <= PromptRange;
		}
		EnsurePrompt();
		_promptLayer.Visible = near;
	}

	/// <summary>Lazily builds the 2D hint (CanvasLayer + Label, anchored bottom-centre).</summary>
	private void EnsurePrompt()
	{
		if (_promptLayer != null) return;

		_promptLayer = new CanvasLayer { Layer = 10, Visible = false };
		AddChild(_promptLayer);

		_promptLabel = new Label
		{
			Text = $"[{KeyName()}]  {PromptText}",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Bottom,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			LabelSettings = new LabelSettings
			{
				FontSize = PromptFontSize,
				FontColor = Colors.White,
				OutlineSize = 6,
				OutlineColor = Colors.Black,
			},
		};
		_promptLayer.AddChild(_promptLabel);
		_promptLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_promptLabel.OffsetBottom = -140f;
	}

	/// <summary>Resolves the key/button bound to <see cref="PushAction"/> in the Input Map for the on-screen prompt.</summary>
	private string KeyName()
	{
		foreach (InputEvent ev in InputMap.ActionGetEvents(PushAction))
		{
			if (ev is InputEventKey k) return k.AsText().Replace(" (Physical)", "");
			if (ev is InputEventMouseButton mb) return $"Mouse {(int)mb.ButtonIndex}";
		}
		return "?";
	}

	/// <summary>
	/// Editor visual: draws a yellow arrow along <see cref="PushDirection"/> with length
	/// <see cref="MaxPushDistance"/>. The arrow node has no owner, so it is not saved to the scene.
	/// </summary>
	private void UpdateDirectionGizmo()
	{
		if (_dirGizmo == null || !IsInstanceValid(_dirGizmo))
		{
			_dirGizmo = new MeshInstance3D
			{
				Mesh = new ImmediateMesh(),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				CustomAabb = new Aabb(new Vector3(-64f, -64f, -64f), new Vector3(128f, 128f, 128f)),
				MaterialOverride = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor = new Color(1f, 0.85f, 0.2f),
					NoDepthTest = true,
				},
			};
			AddChild(_dirGizmo);
		}

		var mesh = (ImmediateMesh)_dirGizmo.Mesh;
		mesh.ClearSurfaces();

		Vector3 dir = PushDirection.Normalized();
		if (dir.LengthSquared() < 0.0001f) return;
		float len = MaxPushDistance > 0f ? MaxPushDistance : 3f;
		Vector3 tip = dir * len;

		Vector3 side = dir.Cross(Vector3.Up);
		if (side.LengthSquared() < 0.001f) side = dir.Cross(Vector3.Right);
		side = side.Normalized() * (len * 0.09f);
		Vector3 up = dir.Cross(side).Normalized() * (len * 0.09f);
		Vector3 back = tip - dir * (len * 0.18f);

		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
		mesh.SurfaceAddVertex(Vector3.Zero); mesh.SurfaceAddVertex(tip);
		mesh.SurfaceAddVertex(tip); mesh.SurfaceAddVertex(back + side);
		mesh.SurfaceAddVertex(tip); mesh.SurfaceAddVertex(back - side);
		mesh.SurfaceAddVertex(tip); mesh.SurfaceAddVertex(back + up);
		mesh.SurfaceAddVertex(tip); mesh.SurfaceAddVertex(back - up);
		mesh.SurfaceEnd();
	}

	/// <summary>Looks up the local player once via a tree search and caches the reference.</summary>
	private NetworkPlayer ResolvePlayer()
	{
		if (_player != null && IsInstanceValid(_player)) return _player;
		_player = FindNetworkPlayer(GetTree()?.Root);
		return _player;
	}

	/// <summary>Recursive depth-first search returning the first NetworkPlayer under <paramref name="n"/>.</summary>
	private static NetworkPlayer FindNetworkPlayer(Node n)
	{
		if (n == null) return null;
		if (n is NetworkPlayer lc) return lc;
		foreach (Node c in n.GetChildren())
		{
			NetworkPlayer r = FindNetworkPlayer(c);
			if (r != null) return r;
		}
		return null;
	}
}
