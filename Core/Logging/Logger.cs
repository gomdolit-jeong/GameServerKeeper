using System.Text;

namespace GameServerKeeper.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <param name="ScopeId">서버 프로필 Id (전역 로그는 null)</param>
/// <param name="ScopeName">서버 표시 이름 (전역 로그는 null)</param>
public sealed record LogEntry(DateTime Time, LogLevel Level, string? ScopeId, string? ScopeName, string Source, string Message);

/// <summary>모든 서비스가 사용하는 로그 인터페이스. 서버별 로거는 자동으로 서버 이름이 붙는다.</summary>
public interface IAppLog
{
    void Write(LogLevel level, string source, string message, Exception? ex = null);
}

/// <summary>아무것도 기록하지 않는 로거 (설정 화면에서 임시 어댑터를 만들 때 등)</summary>
public sealed class NullLog : IAppLog
{
    public static readonly NullLog Instance = new();
    public void Write(LogLevel level, string source, string message, Exception? ex = null) { }
}

public static class AppLogExtensions
{
    public static void Debug(this IAppLog log, string source, string message) => log.Write(LogLevel.Debug, source, message);
    public static void Info(this IAppLog log, string source, string message) => log.Write(LogLevel.Info, source, message);
    public static void Warn(this IAppLog log, string source, string message) => log.Write(LogLevel.Warn, source, message);
    public static void Error(this IAppLog log, string source, string message, Exception? ex = null) => log.Write(LogLevel.Error, source, message, ex);
}

/// <summary>
/// UI 로그 창과 일별 로그 파일(logs/yyyy-MM-dd.log)에 동시에 기록하는 스레드 안전 로거.
/// 파일에는 [서버이름/소스] 형태로 기록되므로 서버가 여러 개여도 한 파일에서 구분할 수 있다.
/// </summary>
public sealed class Logger : IAppLog
{
    private readonly object _fileLock = new();

    public event Action<LogEntry>? EntryWritten;

    public Logger(string logDir)
    {
        LogDirectory = logDir;
        Directory.CreateDirectory(logDir);
    }

    public string LogDirectory { get; }

    /// <summary>서버별 로거. 이름은 Func 로 받아서 서버 이름을 바꿔도 즉시 반영된다.</summary>
    public IAppLog ForScope(string scopeId, Func<string> scopeName) => new ScopedLog(this, scopeId, scopeName);

    public void Write(LogLevel level, string source, string message, Exception? ex = null) =>
        WriteCore(null, null, level, source, message, ex);

    private void WriteCore(string? scopeId, string? scopeName, LogLevel level, string source, string message, Exception? ex)
    {
        var uiMessage = ex == null ? message : $"{message} ({ex.GetType().Name}: {ex.Message})";
        var entry = new LogEntry(DateTime.Now, level, scopeId, scopeName, source, uiMessage);

        lock (_fileLock)
        {
            try
            {
                var tag = scopeName != null ? $"{scopeName}/{source}" : source;
                var line = $"[{entry.Time:yyyy-MM-dd HH:mm:ss.fff}] [{LevelText(level)}] [{tag}] {uiMessage}";
                if (ex != null) line += Environment.NewLine + ex;
                File.AppendAllText(Path.Combine(LogDirectory, $"{entry.Time:yyyy-MM-dd}.log"), line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 로그 파일 기록 실패가 앱을 죽여서는 안 됨
            }
        }

        try { EntryWritten?.Invoke(entry); } catch { /* UI 쪽 예외 무시 */ }
    }

    public static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "???"
    };

    public static string FormatForUi(LogEntry e, bool includeScope)
    {
        var tag = includeScope && e.ScopeName != null ? $"{e.ScopeName}/{e.Source}" : e.Source;
        return $"[{e.Time:HH:mm:ss}] [{LevelText(e.Level)}] [{tag}] {e.Message}";
    }

    private sealed class ScopedLog(Logger root, string scopeId, Func<string> scopeName) : IAppLog
    {
        public void Write(LogLevel level, string source, string message, Exception? ex = null) =>
            root.WriteCore(scopeId, scopeName(), level, source, message, ex);
    }
}
