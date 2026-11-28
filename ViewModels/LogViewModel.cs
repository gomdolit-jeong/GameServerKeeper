using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Services;

namespace GameServerKeeper.ViewModels;

public enum LogKind { Normal, Warn, Error, SteamCmd, Player }

/// <summary>로그 한 줄. 같은 내용의 줄이 여러 개여도 선택이 꼬이지 않도록 record 가 아닌 class.</summary>
public sealed class LogLine(string text, LogKind kind)
{
    public string Text { get; } = text;
    public LogKind Kind { get; } = kind;
}

/// <summary>로그 창: 줄 수 제한, 줄바꿈 켜기/끄기, 복사, 화면 지우기</summary>
public sealed partial class LogViewModel : ObservableObject
{
    private const int MaxLines = 3000;
    private readonly bool _includeScope;
    private readonly IShellService _shell;

    public LogViewModel(bool includeScope, IShellService shell)
    {
        _includeScope = includeScope;
        _shell = shell;
    }

    public ObservableCollection<LogLine> Lines { get; } = new();

    /// <summary>긴 로그(경로, SteamCMD 출력 등)가 잘리지 않도록 기본은 줄바꿈</summary>
    [ObservableProperty] private bool _wordWrap = true;

    public void Append(LogEntry e)
    {
        var kind = e.Level switch
        {
            LogLevel.Error => LogKind.Error,
            LogLevel.Warn => LogKind.Warn,
            _ when e.Source == "SteamCMD" => LogKind.SteamCmd,
            _ when e.Source == "Player" => LogKind.Player,
            _ => LogKind.Normal
        };
        Lines.Add(new LogLine(Logger.FormatForUi(e, _includeScope), kind));
    }

    /// <summary>배치 추가 후 1회 호출: 오래된 줄 정리</summary>
    public void Trim()
    {
        while (Lines.Count > MaxLines) Lines.RemoveAt(0);
    }

    /// <summary>선택한 줄이 있으면 그 줄만, 없으면 전체 복사</summary>
    [RelayCommand]
    private void Copy(IList? selected)
    {
        IEnumerable<LogLine> lines = selected is { Count: > 0 }
            ? selected.OfType<LogLine>().OrderBy(l => Lines.IndexOf(l))
            : Lines;
        var text = string.Join(Environment.NewLine, lines.Select(l => l.Text));
        if (text.Length > 0) _shell.SetClipboard(text);
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();
}
