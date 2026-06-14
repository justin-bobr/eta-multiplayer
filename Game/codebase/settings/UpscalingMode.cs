namespace Vantix.Config;

/// <summary>3D upscaler. Bilinear = plain stretch, Fsr1 = spatial-only, Fsr2 = temporal with built-in AA.
/// When Fsr2 is active the separate TAA/SMAA/FXAA passes are bypassed.</summary>
public enum UpscalingMode { Bilinear, Fsr1, Fsr2 }
