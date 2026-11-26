using System.Diagnostics;
using System.Runtime.InteropServices;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Core.Services;

public sealed class ServerExitedEventArgs : EventArgs
{
    public int Pid { get; init; }
    public int? ExitCode { get; init; }
    /// <summary>매니저가 종료를 요청한 경우 true (크래시 아님)</summary>
    public bool Intentional { get; init; }
}

/// <summary>
/// 서버 1개의 프로세스 실행/추적.
/// 실행 파일(런처, bat 등)을 실행한 뒤, 그 자손 중 <c>trackedProcessName</c> 프로세스를 찾아 "실제 서버"로 추적한다.
/// (예: PalServer.exe → PalServer-Win64-Shipping-Cmd.exe, start.bat → cmd.exe → server.exe)
/// 메모리 측정/크래시 감시/강제 종료는 모두 실제 서버 프로세스 기준이다.
/// </summary>
public sealed class ProcessManager : IDisposable
{
    private const string Src = "Process";
    private const int MaxAncestorDepth = 4;

    private readonly IAppLog _log;
    private readonly object _sync = new();
    private Process? _launcher;
    private Process? _server;
    private volatile bool _intentionalStop;

    public event EventHandler<ServerExitedEventArgs>? ServerExited;

    public ProcessManager(IAppLog log) => _log = log;

    public bool IsRunning
    {
        get
        {
            var p = _server;
            return p != null && !SafeHasExited(p);
        }
    }

    public int? ServerPid
    {
        get { try { return _server?.Id; } catch { return null; } }
    }

    public DateTime? ServerStartTime
    {
        get { try { return _server?.StartTime; } catch { return null; } }
    }

    /// <summary>실행 파일을 띄우고 실제 서버 프로세스가 나타날 때까지 대기</summary>
    public async Task<bool> StartAsync(string exePath, string args, string trackedProcessName, bool hideConsole,
        TimeSpan detectTimeout, CancellationToken ct)
    {
        if (IsRunning)
        {
            _log.Warn(Src, "서버가 이미 실행 중입니다.");
            return true;
        }
        if (!File.Exists(exePath))
        {
            _log.Error(Src, $"서버 실행 파일을 찾을 수 없습니다: {exePath}");
            return false;
        }

        CleanupLauncher(); // 이전 런처가 남아 있다면 정리

        var workDir = Path.GetDirectoryName(exePath)!;
        var psi = new ProcessStartInfo(exePath, args)
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = hideConsole
        };

        _intentionalStop = false;
        Process launcher;
        try
        {
            launcher = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start 가 null 을 반환");
        }
        catch (Exception ex)
        {
            _log.Error(Src, $"{Path.GetFileName(exePath)} 실행 실패", ex);
            return false;
        }

        _log.Info(Src, $"{Path.GetFileName(exePath)} 실행됨 (PID {launcher.Id}) {args}");

        // 실행 파일 자체가 서버인 경우
        if (string.Equals(SafeName(launcher), trackedProcessName, StringComparison.OrdinalIgnoreCase))
        {
            Attach(launcher);
            return true;
        }

        _launcher = launcher;
        var deadline = DateTime.UtcNow + detectTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var child = FindDescendantOf(launcher.Id, trackedProcessName);
            if (child != null)
            {
                Attach(child);
                _log.Info(Src, $"실제 서버 프로세스 감지: {trackedProcessName} (PID {child.Id})");
                return true;
            }

            if (SafeHasExited(launcher))
            {
                // 런처가 먼저 끝난 경우 — 경로 기준으로 고아 서버 프로세스를 찾는다
                var orphan = FindByPath(workDir, trackedProcessName);
                if (orphan != null)
                {
                    Attach(orphan);
                    _log.Info(Src, $"실제 서버 프로세스 감지(경로 기준): PID {orphan.Id}");
                    return true;
                }
                _log.Error(Src, $"런처가 {trackedProcessName} 프로세스를 띄우지 못하고 종료됨 (ExitCode {SafeExitCode(launcher)})");
                return false;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        _log.Error(Src, $"{detectTimeout.TotalSeconds:0}초 안에 {trackedProcessName} 프로세스를 찾지 못했습니다. (게임 설정의 추적 프로세스 이름 확인)");
        CleanupLauncher();
        return false;
    }

    /// <summary>매니저 재실행 시 이미 떠 있는 서버 프로세스(설치 폴더 기준)에 붙는다.</summary>
    public bool TryAttachExisting(string installDir, string trackedProcessName)
    {
        var p = FindByPath(installDir, trackedProcessName);
        if (p == null) return false;

        _intentionalStop = false;
        Attach(p);
        _log.Info(Src, $"이미 실행 중인 서버에 연결했습니다 (PID {p.Id}).");
        return true;
    }

    /// <summary>다음 프로세스 종료를 "의도된 종료"로 표시 → 크래시 복구가 동작하지 않음</summary>
    public void MarkIntentionalStop() => _intentionalStop = true;

    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (IsRunning)
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>최후 수단: 서버 및 런처 프로세스 트리 강제 종료</summary>
    public void Kill()
    {
        _intentionalStop = true;
        var server = _server;
        try
        {
            if (server != null && !SafeHasExited(server))
            {
                _log.Warn(Src, $"서버 프로세스 강제 종료(Kill) PID {server.Id}");
                server.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) { _log.Error(Src, "서버 프로세스 Kill 실패", ex); }

        CleanupLauncher();
    }

    /// <summary>런처(예: bat 의 cmd.exe "일괄 작업을 끝내시겠습니까?" 대기)가 남지 않도록 정리</summary>
    public void CleanupLauncher()
    {
        var l = _launcher;
        _launcher = null;
        if (l == null) return;
        try { if (!SafeHasExited(l)) l.Kill(entireProcessTree: true); }
        catch { /* 무시 */ }
        finally { l.Dispose(); }
    }

    public long? GetWorkingSet()
    {
        var p = _server;
        if (p == null) return null;
        try
        {
            p.Refresh();
            return p.HasExited ? null : p.WorkingSet64;
        }
        catch { return null; }
    }

    // ───────────── 내부 ─────────────

    private void Attach(Process p)
    {
        lock (_sync)
        {
            if (_server != null) _server.Exited -= OnServerExited;
            _server = p;
            p.Exited += OnServerExited;
            p.EnableRaisingEvents = true; // 이미 종료된 상태라도 Exited 가 발생함
        }
    }

    private void OnServerExited(object? sender, EventArgs e)
    {
        if (sender is not Process p) return;
        lock (_sync)
        {
            if (!ReferenceEquals(p, _server)) return;
            _server = null;
        }
        p.Exited -= OnServerExited;

        var args = new ServerExitedEventArgs { Pid = SafeId(p), ExitCode = SafeExitCode(p), Intentional = _intentionalStop };
        p.Dispose();

        try { ServerExited?.Invoke(this, args); }
        catch (Exception ex) { _log.Error(Src, "ServerExited 처리 중 오류", ex); }
    }

    private static Process? FindDescendantOf(int ancestorPid, string processName)
    {
        Process? found = null;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (found == null && IsDescendantOf(p.Id, ancestorPid)) found = p;
            else p.Dispose();
        }
        return found;
    }

    private static bool IsDescendantOf(int pid, int ancestorPid)
    {
        int current = pid;
        for (int depth = 0; depth < MaxAncestorDepth; depth++)
        {
            var parent = GetParentPid(current);
            if (parent is not int pp || pp == 0) return false;
            if (pp == ancestorPid) return true;
            current = pp;
        }
        return false;
    }

    private static Process? FindByPath(string dir, string processName)
    {
        string root;
        try { root = Path.GetFullPath(dir).TrimEnd('\\') + "\\"; }
        catch { return null; }

        Process? found = null;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (found == null)
            {
                var path = TryGetPath(p);
                if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    found = p;
                    continue;
                }
            }
            p.Dispose();
        }
        return found;
    }

    private static string? TryGetPath(Process p)
    {
        try { return p.MainModule?.FileName; } catch { return null; }
    }

    private static bool SafeHasExited(Process p) { try { return p.HasExited; } catch { return true; } }
    private static int? SafeExitCode(Process p) { try { return p.HasExited ? p.ExitCode : null; } catch { return null; } }
    private static int SafeId(Process p) { try { return p.Id; } catch { return -1; } }
    private static string SafeName(Process p) { try { return p.ProcessName; } catch { return ""; } }

    // ── 부모 PID 조회 (WMI 없이 ntdll 사용) ──

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    private static int? GetParentPid(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var pbi = new ProcessBasicInformation();
            int status = NtQueryInformationProcess(p.Handle, 0, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            return status == 0 ? pbi.InheritedFromUniqueProcessId.ToInt32() : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        // 매니저 종료가 서버를 죽이지는 않는다 — 핸들만 정리
        lock (_sync)
        {
            if (_server != null)
            {
                _server.Exited -= OnServerExited;
                _server.Dispose();
                _server = null;
            }
        }
        _launcher?.Dispose();
        _launcher = null;
    }
}
