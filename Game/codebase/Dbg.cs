using System.Runtime.CompilerServices;
using Godot;

/// <summary>Central debug-logging gate driven by the "global/debug" project setting. <see cref="Enabled"/>
/// is read once and cached; real errors still go through GD.PrintErr ungated. <see cref="Print"/> uses an
/// interpolated-string handler so `Dbg.Print($"hp={hp}")` allocates nothing when disabled.</summary>
public static class Dbg
{
	/// <summary>True when the project setting "global/debug" is active.</summary>
	public static bool Enabled { get; } =
		ProjectSettings.GetSetting("global/debug", false).AsBool();

	/// <summary>Like GD.Print but only emits when <see cref="Enabled"/>; zero-cost when disabled via the handler.</summary>
	public static void Print(ref PrintInterpolatedStringHandler handler)
	{
		if (Enabled) GD.Print(handler.ToStringAndClear());
	}

	/// <summary>Plain-string overload for non-interpolated literals.</summary>
	public static void Print(string message)
	{
		if (Enabled) GD.Print(message);
	}
}

/// <summary>InterpolatedStringHandler for <see cref="Dbg.Print"/>. The <c>out bool shouldAppend</c>
/// constructor convention lets the compiler skip all Append calls when <see cref="Dbg.Enabled"/> is false (zero alloc).</summary>
[InterpolatedStringHandler]
public ref struct PrintInterpolatedStringHandler
{
	private DefaultInterpolatedStringHandler _inner;
	private readonly bool _enabled;

	public PrintInterpolatedStringHandler(int literalLength, int formattedCount, out bool shouldAppend)
	{
		_enabled = Dbg.Enabled;
		shouldAppend = _enabled;
		_inner = _enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
	}

	public void AppendLiteral(string s) { if (_enabled) _inner.AppendLiteral(s); }
	public void AppendFormatted<T>(T value) { if (_enabled) _inner.AppendFormatted(value); }
	public void AppendFormatted<T>(T value, string format) { if (_enabled) _inner.AppendFormatted(value, format); }
	public void AppendFormatted(string value) { if (_enabled) _inner.AppendFormatted(value); }
	public void AppendFormatted(System.ReadOnlySpan<char> value) { if (_enabled) _inner.AppendFormatted(value); }

	public string ToStringAndClear() => _enabled ? _inner.ToStringAndClear() : "";
}
