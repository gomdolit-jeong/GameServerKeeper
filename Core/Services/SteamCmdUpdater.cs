using System.Collections.Concurrent;
using System.Diagnostics;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Core.Services;

/// <summary>
/// SteamCMD 로 서버를 설치/업데이트한다. 반드시 해당 서버가 꺼진 상태에서 호출할 것.
/// 같은 steamcmd.exe 를 여러 서버가 동시에 실행하면 충돌하므로 steamcmd 경로별로 직렬화한다.
/// (예: 04:00 에 서버 3대가 동시에 정기 재시작 → 업데이트는 한 대씩 순서대로)
/// </summary>
public sealed class SteamCmdUpdater
{
    private const string Src = "SteamCMD";
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    private readonly IAppLog _log;

    public SteamCmdUpdater(IAppLog log) => _log = log;

    public async Task<bool> UpdateAsync(string steamCmdPath, string installDir, int appId, bool validate, CancellationToken ct)
    {
        if (appId <= 0)
        {
            _log.Info(Src, "Steam App ID 가 설정되지 않아 업데이트를 건너뜁니다.");
            return true;
        }
        if (!File.Exists(steamCmdPath))
        {
            _log.Error(Src, $"steamcmd.exe 를 찾을 수 없습니다: {steamCmdPath}");
            return false;
        }

        var gate = Locks.GetOrAdd(Path.GetFullPath(steamCmdPath), _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _log.Info(Src, "다른 서버의 SteamCMD 작업이 끝나기를 기다리는 중...");
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }

        try
        {
            var args = $"+force_install_dir \"{installDir}\" +login anonymous " +
                       $"+app_update {appId}{(validate ? " validate" : "")} +quit";

            // SteamCMD 는 자기 자신을 업데이트한 직후 실패 코드로 끝나는 경우가 있어 1회 재시도
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                _log.Info(Src, $"업데이트 시작 (App {appId}, 시도 {attempt}/2)...");
                var sw = Stopwatch.StartNew();
                var (exitCode, sawSuccess) = await RunAsync(steamCmdPath, args, ct).ConfigureAwait(false);

                if (exitCode == 0 || sawSuccess)
                {
                    _log.Info(Src, $"업데이트 완료 ({sw.Elapsed.TotalSeconds:0}초, ExitCode {exitCode})");
                    return true;
                }
                _log.Warn(Src, $"업데이트 실패 (ExitCode {exitCode})");
            }
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(int ExitCode, bool SawSuccess)> RunAsync(string exe, string args, CancellationToken ct)
    {
        bool sawSuccess = false;
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) =>
        {
            var line = e.Data?.Trim();
            if (string.IsNullOrEmpty(line)) return;
            if (line.Contains("Success! App", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("already up to date", StringComparison.OrdinalIgnoreCase))
                sawSuccess = true;
            _log.Info(Src, line);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            var line = e.Data?.Trim();
            if (!string.IsNullOrEmpty(line)) _log.Warn(Src, line);
        };

        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            _log.Error(Src, "steamcmd 실행 실패", ex);
            return (-1, false);
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(MaxDuration);
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* 무시 */ }
            _log.Error(Src, ct.IsCancellationRequested ? "업데이트가 취소되었습니다." : $"업데이트가 {MaxDuration.TotalMinutes:0}분을 넘겨 중단했습니다.");
            return (-1, false);
        }

        p.WaitForExit(); // 남은 stdout 이벤트 flush
        return (p.ExitCode, sawSuccess);
    }
}
