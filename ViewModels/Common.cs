using GameServerKeeper.Core.Server;

namespace GameServerKeeper.ViewModels;

/// <summary>
/// 화면 색을 ViewModel 이 직접 고르지 않도록 쓰는 "의미 색". 실제 색은 View(ToneBrushConverter)가 정한다.
/// </summary>
public enum Tone { Normal, Muted, Faint, Accent, Purple, Success, Warning, Danger, Info, Neutral }

/// <summary>상태 타일 하나에 표시할 값</summary>
/// <param name="Progress">0~1 막대. null 이면 막대 없음</param>
public sealed record StatInfo(string Value, string? Caption = null, Tone Tone = Tone.Normal, double? Progress = null, Tone ProgressTone = Tone.Purple)
{
    public static readonly StatInfo Empty = new("-");
}

/// <summary>Segoe Fluent Icons / MDL2 Assets 글리프 (XAML 에서는 x:Static 으로 사용)</summary>
public static class Glyphs
{
    public const string Play = "";
    public const string Stop = "";
    public const string Restart = "";
    public const string Save = "";
    public const string Download = "";
    public const string Announce = "";
    public const string Folder = "";
    public const string Settings = "";
    public const string Add = "";
    public const string Edit = "";
    public const string Delete = "";
    public const string Home = "";
    public const string Info = "";
    public const string People = "";
    public const string Clock = "";
    public const string Calendar = "";
    public const string History = "";
    public const string Globe = "";
    public const string Game = "";
    public const string Memory = "";
    public const string View = "";
    public const string Document = "";
    public const string Server = "";
    public const string Warning = "";
    public const string ChevronDown = "";
}

/// <summary>표시용 문자열 변환</summary>
public static class Format
{
    public static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB";

    public static string Uptime(TimeSpan up) =>
        up.TotalDays >= 1 ? $"{(int)up.TotalDays}일 {up.Hours}시간 {up.Minutes}분"
        : up.TotalHours >= 1 ? $"{up.Hours}시간 {up.Minutes}분"
        : $"{up.Minutes}분 {up.Seconds}초";

    public static string Relative(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "방금 전";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}분 전";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}시간 전";
        return $"{(int)d.TotalDays}일 전";
    }

    public static string Upcoming(DateTime t)
    {
        var d = t - DateTime.Now;
        var day = t.Date == DateTime.Today ? "오늘" : t.Date == DateTime.Today.AddDays(1) ? "내일" : t.ToString("MM-dd");
        return d.TotalHours < 1 ? $"{day} {t:HH:mm} ({Math.Max(0, (int)d.TotalMinutes)}분 후)" : $"{day} {t:HH:mm}";
    }

    public static (string Text, Tone Tone) State(ServerState s) => s switch
    {
        ServerState.Running => ("실행 중", Tone.Success),
        ServerState.Starting => ("시작 중", Tone.Warning),
        ServerState.Stopping => ("종료 중", Tone.Warning),
        ServerState.Updating => ("업데이트 중", Tone.Info),
        ServerState.BackingUp => ("백업 중", Tone.Info),
        _ => ("중지됨", Tone.Neutral)
    };

    /// <summary>메모리 사용 비율에 따른 막대 색</summary>
    public static Tone MemoryTone(double ratio) => ratio >= 0.9 ? Tone.Danger : ratio >= 0.75 ? Tone.Warning : Tone.Purple;
}
