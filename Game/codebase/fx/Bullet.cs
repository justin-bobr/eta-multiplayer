using Godot;

[GlobalClass]
public partial class Bullet : RigidBody3D
{
	public void Launch(Transform3D spawnTransform, Vector3 linearVelocity, Vector3 angularVelocity, float lifetime)
	{
		GlobalTransform = spawnTransform;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		Freeze = false;
		Visible = true;
		LinearVelocity = linearVelocity;
		AngularVelocity = angularVelocity;
		GetTree().CreateTimer(lifetime).Timeout += Reset;
	}

	public void Reset()
	{
		Visible = false;
		Freeze = true;
	}
}
