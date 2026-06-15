using System;
using System.Diagnostics;
using System.IO;

namespace Vantix.Patcher;

public static class GameLauncher
{
    private static string GameRelativePath =>
        OperatingSystem.IsWindows() ? "game/vantix.exe" : "game/vantix.x86_64";

    public static string GameExePath =>
        Path.Combine(AppContext.BaseDirectory, GameRelativePath);

    public static bool Exists => File.Exists(GameExePath);

    public static void Launch()
    {
        var exe = GameExePath;
        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        });
    }
}
