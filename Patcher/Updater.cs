using System;
using System.Threading.Tasks;
using Velopack;

namespace Vantix.Patcher;

public sealed class Updater
{
    private const string FeedUrl = "https://REPLACE-ME.r2.dev";

    private readonly UpdateManager _mgr;

    public Updater() => _mgr = new UpdateManager(FeedUrl);

    public bool IsInstalled => _mgr.IsInstalled;

    public async Task<bool> CheckAndApplyAsync(IProgress<string> status, IProgress<int> percent)
    {
        if (!_mgr.IsInstalled)
        {
            status.Report("Development build - update check skipped.");
            return false;
        }

        status.Report("Checking for updates...");
        UpdateInfo? info;
        try
        {
            info = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status.Report($"Update check failed: {ex.Message}");
            return false;
        }

        if (info is null)
        {
            status.Report("Up to date.");
            return false;
        }

        status.Report($"Downloading update {info.TargetFullRelease.Version}...");
        await _mgr.DownloadUpdatesAsync(info, percent.Report).ConfigureAwait(false);

        status.Report("Installing update...");
        _mgr.ApplyUpdatesAndRestart(info);
        return true;
    }
}
