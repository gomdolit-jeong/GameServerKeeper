using System.Diagnostics;
using System.Windows;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Services;

public sealed class ShellService(IAppLog log) : IShellService
{
    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { log.Warn("UI", $"폴더 열기 실패: {ex.Message}"); }
    }

    public void SetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { log.Warn("UI", $"클립보드 복사 실패: {ex.Message}"); }
    }
}
