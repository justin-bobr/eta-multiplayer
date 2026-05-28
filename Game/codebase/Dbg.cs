using System.Runtime.CompilerServices;
using Godot;

/// <summary>
/// Central debug-logging gate. Replaces scattered GD.Print calls and per-class
/// [Export] bool Debug flags with a single project setting: "global/debug"
/// (Project Settings -> General -> Global).
///
/// <see cref="Enabled"/> is read once and cached. Real errors still go through
/// GD.PrintErr ungated so they remain visible.
///
/// <see cref="Print"/> nutzt einen <see cref="InterpolatedStringHandlerAttribute"/> sodass
/// `Dbg.Print($"hp={hp}")` die String-Interpolation KOMPLETT überspringt wenn Dbg disabled
/// ist — kein StringBuilder, keine Boxing-Allocs, kein Format-Call. Performance-Charakteristik
/// identisch zum manuell-geschriebenen `if (Dbg.Enabled) GD.Print($"...")`, aber sauberer Code.
/// </summary>
public static class Dbg
{
	/// <summary>True when the project setting "global/debug" is active.</summary>
	public static bool Enabled { get; } =
		ProjectSettings.GetSetting("global/debug", false).AsBool();

	/// <summary>Like GD.Print but only emits when <see cref="Enabled"/> is true. Interpolated
	/// strings sind dank <see cref="PrintInterpolatedStringHandler"/> zero-cost wenn disabled.</summary>
	public static void Print(ref PrintInterpolatedStringHandler handler)
	{
		if (Enabled) GD.Print(handler.ToStringAndClear());
	}

	/// <summary>Plain-string overload for non-interpolated literals (z.B. `Dbg.Print("hello")`).
	/// Wird vom Compiler bevorzugt wenn das Argument ein konstanter String ist.</summary>
	public static void Print(string message)
	{
		if (Enabled) GD.Print(message);
	}
}

/// <summary>InterpolatedStringHandler für <see cref="Dbg.Print"/> — die `out bool isEnabled`
/// Constructor-Convention signalisiert dem Compiler ob die String-Interpolation überhaupt
/// ausgeführt werden soll. Bei <see cref="Dbg.Enabled"/> false: false → Compiler skipt alle
/// AppendLiteral/AppendFormatted Calls komplett (= zero alloc).</summary>
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
	// Kein IFormattable-Constraint: Godot's Vector3 etc. implementieren das nicht. DefaultInterp-
	// StringHandler.AppendFormatted<T>(T, string?) macht den runtime-check selbst (IFormattable
	// → format string applied, sonst → ignored, ToString() fallback). Funktioniert für alle Types.
	public void AppendFormatted<T>(T value, string format) { if (_enabled) _inner.AppendFormatted(value, format); }
	public void AppendFormatted(string value) { if (_enabled) _inner.AppendFormatted(value); }
	public void AppendFormatted(System.ReadOnlySpan<char> value) { if (_enabled) _inner.AppendFormatted(value); }

	public string ToStringAndClear() => _enabled ? _inner.ToStringAndClear() : "";
}
