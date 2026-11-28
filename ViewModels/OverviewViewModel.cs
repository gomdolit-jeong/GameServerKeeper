using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.ViewModels;

/// <summary>카드 목록 끝의 "서버 추가" 카드</summary>
public sealed class AddCardItem(ICommand command)
{
    public ICommand Command { get; } = command;
}

/// <summary>전체 현황: 요약 타일 + 서버 카드 + 전체 로그</summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly AddCardItem _addCard;

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        _addCard = new AddCardItem(main.AddServerCommand);
        Cards.Add(_addCard);
    }

    public LogViewModel Log => _main.GlobalLog;

    /// <summary>서버 카드(<see cref="ServerViewModel"/>) + 마지막에 "서버 추가" 카드</summary>
    public ObservableCollection<object> Cards { get; } = new();

    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private StatInfo _runningStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _playersStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _memoryStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _nextRestartStat = StatInfo.Empty;

    public void SyncCards()
    {
        var wanted = _main.Servers.Cast<object>().Append(_addCard).ToList();
        if (Cards.SequenceEqual(wanted)) return;
        Cards.Clear();
        foreach (var c in wanted) Cards.Add(c);
    }

    public void Refresh()
    {
        var statuses = _main.Servers.Select(v => v.Server.LatestStatus).OfType<ServerStatus>().ToList();
        int total = _main.Servers.Count;
        int running = statuses.Count(s => s.ProcessRunning);

        Subtitle = total == 0 ? "등록된 서버가 없어요. '서버 추가'로 시작하세요." : $"서버 {total}대를 관리하고 있어요.";

        RunningStat = new StatInfo($"{running} / {total}", running == total && total > 0 ? "모두 실행 중" : $"{total - running}대 중지됨",
            Progress: total == 0 ? 0 : (double)running / total, ProgressTone: Tone.Success);

        var withPlayers = statuses.Where(s => s.Capabilities.HasFlag(GameCapabilities.PlayerList) && s.Players != null).ToList();
        PlayersStat = new StatInfo($"{withPlayers.Sum(s => s.Players!.Count)}명", $"서버 {withPlayers.Count}대 기준");

        long mem = statuses.Sum(s => s.WorkingSetBytes ?? 0);
        MemoryStat = new StatInfo(mem > 0 ? Format.Gb(mem) : "-", running > 0 ? $"실행 중인 서버 {running}대" : "실행 중인 서버 없음");

        var next = statuses.Where(s => s.NextScheduledRestart != null).OrderBy(s => s.NextScheduledRestart).FirstOrDefault();
        NextRestartStat = next != null
            ? new StatInfo(Format.Upcoming(next.NextScheduledRestart!.Value), next.Name)
            : new StatInfo("없음", "정기 재시작을 켠 서버가 없어요", Tone.Faint);
    }
}
