/// <summary>Spectator camera mode for a <see cref="PuppetPlayer"/>: no camera, third-person follow, or
/// first-person through the puppet's eyes.</summary>
public enum SpectateMode
{
	/// <summary>Default — no camera active; the puppet is simply rendered in the world.</summary>
	None,
	/// <summary>Follow camera — third-person camera enabled.</summary>
	Tps,
	/// <summary>First-person — sees through the puppet's eyes without a viewmodel.</summary>
	Fps,
}
