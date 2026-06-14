using System.Diagnostics;
using Godot;

namespace Vantix;

/// <summary>Autoload that raises process priority to High on startup (smoother frame-time under load). Realtime
/// is avoided — it can destabilise the OS and needs admin. High needs no admin on Windows; on Linux it maps to
/// nice -10 (requires CAP_SYS_NICE).</summary>
public partial class ProcessPriorityBoost : Node
{
	public override void _Ready()
	{
		try
		{
			var proc = Process.GetCurrentProcess();
			ProcessPriorityClass old = proc.PriorityClass;
			proc.PriorityClass = ProcessPriorityClass.High;
			GD.Print($"[ProcessPriorityBoost] PID {proc.Id} priority: {old} → {proc.PriorityClass}");
		}
		catch (System.Exception e)
		{
			GD.PushWarning(
				$"[ProcessPriorityBoost] Failed to set High priority: {e.Message} (on Linux needs CAP_SYS_NICE or root; on Windows should work without admin)"
			);
		}
	}
}
