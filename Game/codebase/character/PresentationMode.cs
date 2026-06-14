namespace Vantix.Character;

/// <summary>Which driver owns a given <see cref="NetworkPlayer"/> view: the local player, a
/// remote puppet, or a headless server agent.</summary>
public enum PresentationMode { Local, Remote, Server }
