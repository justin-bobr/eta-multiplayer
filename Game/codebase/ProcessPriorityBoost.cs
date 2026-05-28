using Godot;
using System.Diagnostics;

/// <summary>
/// Autoload: setzt die Process-Priority des Game-Prozesses (Server wie Client) auf High beim
/// Start. Default ist Normal (Process-Klasse 0x20) — High (0x80) heißt Windows-Scheduler gibt
/// uns größere Time-Slices, weniger Preemption von Background-Tasks (Browser, Discord, etc).
///
/// Effekt: spürbar smootherer Frame-Time besonders bei sichtbar konkurrierenden Apps. Kein
/// CPU-Stealing für Hintergrund-Sachen mitten in einem Physics-Step.
///
/// Realtime (0x100) bewusst NICHT verwendet — kann das OS instabil machen und braucht admin.
/// High geht ohne admin auf Windows. Auf Linux mappt auf nice -10 (= braucht CAP_SYS_NICE).
///
/// Wird via project.godot [autoload] geladen damit es VOR allen anderen Nodes greift.
/// </summary>
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
			GD.PushWarning($"[ProcessPriorityBoost] Failed to set High priority: {e.Message} (auf Linux brauchst CAP_SYS_NICE oder root; auf Windows sollte's ohne admin gehen)");
		}
	}
}
