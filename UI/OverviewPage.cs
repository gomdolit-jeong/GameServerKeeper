using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.UI;

/// <summary>전체 현황: 요약 타일 + 서버 카드 + 전체 로그</summary>
public sealed class OverviewPage : UserControl
{
    private readonly ServerManager _manager;
    private readonly Action<ServerInstance> _open;
    private readonly Action<ServerInstance> _start;
    private readonly Action<ServerInstance> _stop;
    private readonly Action _add;
    private readonly ToolTip _tip = Theme.CreateToolTip();

    private readonly Label _lblSub = new() { AutoSize = true, Font = Theme.Font(9.5f), ForeColor = Theme.TextMuted, Location = new Point(2, 42) };
    private readonly StatTile _tRunning = new("실행 중인 서버", Glyphs.Server, Theme.Success);
    private readonly StatTile _tPlayers = new("전체 접속자", Glyphs.People, Theme.Info);
    private readonly StatTile _tMemory = new("메모리 합계", Glyphs.Memory, Color.FromArgb(124, 58, 237));
    private readonly StatTile _tNext = new("다음 정기 재시작", Glyphs.Calendar, Theme.Warning);
    private readonly FlowLayoutPanel _cards = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
        BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, 8)
    };
    private readonly Dictionary<string, ServerCard> _cardMap = new();
    private readonly AddCard _addCard = new("서버 추가") { Size = new Size(330, 186), Margin = new Padding(0, 0, 14, 14) };

    public OverviewPage(ServerManager manager, LogView globalLog,
        Action<ServerInstance> open, Action<ServerInstance> start, Action<ServerInstance> stop, Action add)
    {
        _manager = manager;
        _open = open;
        _start = start;
        _stop = stop;
        _add = add;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Bg };
        header.Controls.Add(new Label { Text = "전체 현황", AutoSize = true, Font = Theme.Font(18f, FontStyle.Bold), ForeColor = Theme.Text, Location = new Point(0, 0) });
        header.Controls.Add(_lblSub);

        var tiles = new TableLayoutPanel { Dock = DockStyle.Top, Height = 108, ColumnCount = 4, BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, 12) };
        StatTile[] all = [_tRunning, _tPlayers, _tMemory, _tNext];
        for (int i = 0; i < all.Length; i++)
        {
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            all[i].Dock = DockStyle.Fill;
            all[i].Margin = new Padding(0, 0, i < all.Length - 1 ? 12 : 0, 0);
            tiles.Controls.Add(all[i], i, 0);
        }
        _tip.SetToolTip(_tMemory, "실행 중인 모든 서버 프로세스의 메모리 사용량 합계예요.");
        _tip.SetToolTip(_tPlayers, "접속자 조회를 지원하는 서버들의 접속자 합계예요.");

        _addCard.Click += (_, _) => _add();
        _tip.SetToolTip(_addCard, "새 데디 서버를 등록해요. 팰월드 외 게임은 '범용'으로 추가할 수 있어요.");

        var logCard = new Card { Dock = DockStyle.Fill, FillColor = Theme.LogBg, BorderColor = Theme.LogBg, Padding = new Padding(16, 12, 8, 12) };
        logCard.Controls.Add(globalLog);
        logCard.Controls.Add(Ui.CardHeader("전체 로그", Glyphs.Document, Color.FromArgb(214, 218, 226), Theme.LogBg));

        Controls.Add(logCard);
        Controls.Add(_cards);
        Controls.Add(tiles);
        Controls.Add(header);
        SyncCards();
    }

    /// <summary>서버 추가/삭제 시 카드 목록 맞추기</summary>
    public void SyncCards()
    {
        _cards.SuspendLayout();
        foreach (var id in _cardMap.Keys.Where(id => !ReferenceEquals(_manager.Find(id), _cardMap[id].Server)).ToList())
        {
            _cards.Controls.Remove(_cardMap[id]);
            _cardMap[id].Dispose();
            _cardMap.Remove(id);
        }
        foreach (var server in _manager.Servers)
        {
            if (_cardMap.ContainsKey(server.Id)) continue;
            var card = new ServerCard(server, _open, _start, _stop) { Margin = new Padding(0, 0, 14, 14) };
            _cardMap[server.Id] = card;
            _cards.Controls.Add(card);
        }
        // 서버 목록 순서대로, "서버 추가" 카드는 항상 마지막
        int i = 0;
        foreach (var server in _manager.Servers) _cards.Controls.SetChildIndex(_cardMap[server.Id], i++);
        _cards.Controls.Remove(_addCard);
        _cards.Controls.Add(_addCard);
        _cards.ResumeLayout();
    }

    public void Render()
    {
        var statuses = _manager.Servers.Select(s => s.LatestStatus).OfType<ServerStatus>().ToList();
        int total = _manager.Servers.Count;
        int running = statuses.Count(s => s.ProcessRunning);

        _lblSub.Text = total == 0 ? "등록된 서버가 없어요. '서버 추가'로 시작하세요." : $"서버 {total}대를 관리하고 있어요.";

        _tRunning.Set($"{running} / {total}", running == total && total > 0 ? "모두 실행 중" : $"{total - running}대 중지됨",
            null, total == 0 ? 0 : (double)running / total, Theme.Success);

        var withPlayers = statuses.Where(s => s.Capabilities.HasFlag(GameCapabilities.PlayerList) && s.Players != null).ToList();
        _tPlayers.Set($"{withPlayers.Sum(s => s.Players!.Count)}명", $"서버 {withPlayers.Count}대 기준");

        long mem = statuses.Sum(s => s.WorkingSetBytes ?? 0);
        _tMemory.Set(mem > 0 ? Ui.Gb(mem) : "-", running > 0 ? $"실행 중인 서버 {running}대" : "실행 중인 서버 없음");

        var next = statuses.Where(s => s.NextScheduledRestart != null).OrderBy(s => s.NextScheduledRestart).FirstOrDefault();
        if (next != null) _tNext.Set(Ui.Upcoming(next.NextScheduledRestart!.Value), next.Name);
        else _tNext.Set("없음", "정기 재시작을 켠 서버가 없어요", Theme.TextFaint);

        foreach (var card in _cardMap.Values) card.Render();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>전체 현황의 서버 카드</summary>
public sealed class ServerCard : Card
{
    private readonly ServerInstance _server;
    private readonly ModernButton _btnOpen = new("열기", Glyphs.View) { Compact = true };
    private readonly ModernButton _btnAction = new("시작", Glyphs.Play, ButtonVariant.Success) { Compact = true };
    private ServerStatus? _status;

    public ServerCard(ServerInstance server, Action<ServerInstance> open, Action<ServerInstance> start, Action<ServerInstance> stop)
    {
        _server = server;
        Size = new Size(330, 186);
        Cursor = Cursors.Hand;

        _btnOpen.Click += (_, _) => open(server);
        _btnAction.Click += (_, _) => { if (server.IsServerRunning) stop(server); else start(server); };
        Click += (_, _) => open(server);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, Location = new Point(16, Height - 48),
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        _btnAction.Margin = new Padding(0, 0, 8, 0);
        _btnOpen.Margin = Padding.Empty;
        buttons.Controls.AddRange([_btnAction, _btnOpen]);
        Controls.Add(buttons);
    }

    public ServerInstance Server => _server;

    public void Render()
    {
        var s = _server.LatestStatus;
        if (s == null || ReferenceEquals(s, _status)) { if (s?.ProcessRunning == true) Invalidate(); return; }
        _status = s;

        bool idle = s.CurrentOperation == null;
        if (s.ProcessRunning) { _btnAction.Text = "안전 종료"; _btnAction.Glyph = Glyphs.Stop; _btnAction.Variant = ButtonVariant.Danger; }
        else { _btnAction.Text = "시작"; _btnAction.Glyph = Glyphs.Play; _btnAction.Variant = ButtonVariant.Success; }
        _btnAction.Enabled = idle;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = _status;
        if (s == null) return;
        var g = e.Graphics;
        Theme.SetupGraphics(g);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        // 상태 배지 (오른쪽 위)
        var (stateText, fg, bg) = Theme.State(s.State);
        if (s.CurrentOperation != null) (stateText, fg, bg) = ($"{s.CurrentOperation} 중", Theme.Warning, Theme.WarningSoft);
        var pillFont = Theme.Font(8.5f, FontStyle.Bold);
        int pillW = TextRenderer.MeasureText(stateText, pillFont, Size.Empty, TextFormatFlags.NoPadding).Width + 30;
        var pill = new RectangleF(Width - 18 - pillW, 16, pillW, 24);
        using (var path = Theme.RoundRect(pill, 12)) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
        using (var dot = new SolidBrush(fg)) g.FillEllipse(dot, pill.X + 10, pill.Y + 8, 8, 8);
        TextRenderer.DrawText(g, stateText, pillFont, new Rectangle((int)pill.X + 22, (int)pill.Y, pillW - 22, 24), fg, flags | TextFormatFlags.VerticalCenter);

        // 이름 / 게임
        int nameW = (int)pill.X - 18 - 8;
        TextRenderer.DrawText(g, s.Name, Theme.Font(12.5f, FontStyle.Bold), new Rectangle(18, 14, nameW, 26), Theme.Text, flags);
        TextRenderer.DrawText(g, Glyphs.Game, Theme.IconFont(9f), new Rectangle(18, 44, 16, 18), Theme.TextMuted, flags | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, s.GameName, Theme.Font(9f), new Rectangle(38, 44, Width - 56, 18), Theme.TextMuted, flags | TextFormatFlags.VerticalCenter);

        // 메모리 막대
        double ratio = s.WorkingSetBytes is long ws ? (double)ws / Math.Max(1, s.MemoryThresholdBytes) : 0;
        var barColor = ratio >= 0.9 ? Theme.Danger : ratio >= 0.75 ? Theme.Warning : Color.FromArgb(124, 58, 237);
        TextRenderer.DrawText(g, "메모리", Theme.Font(8.75f), new Rectangle(18, 74, 80, 18), Theme.TextMuted, flags);
        var memText = s.WorkingSetBytes is long w2 ? $"{Ui.Gb(w2)} / {Ui.Gb(s.MemoryThresholdBytes)}" : "-";
        TextRenderer.DrawText(g, memText, Theme.Font(8.75f, FontStyle.Bold), new Rectangle(18, 74, Width - 36, 18), Theme.Text, flags | TextFormatFlags.Right);
        var bar = new RectangleF(18, 96, Width - 36, 6);
        using (var path = Theme.RoundRect(bar, 3)) using (var b = new SolidBrush(Theme.NeutralSoft)) g.FillPath(b, path);
        if (bar.Width * ratio >= 6)
        {
            using var path = Theme.RoundRect(bar with { Width = (float)(bar.Width * Math.Min(1, ratio)) }, 3);
            using var b = new SolidBrush(barColor);
            g.FillPath(b, path);
        }

        // 접속자 · 가동 시간
        string players = !s.Capabilities.HasFlag(GameCapabilities.PlayerList) ? "접속자 조회 불가"
            : s.Players != null ? $"접속자 {s.Players.Count}명" : s.ProcessRunning ? "접속자 확인 중" : "접속자 -";
        string uptime = s.ProcessRunning && s.StartTime is DateTime st ? $"가동 {Ui.Uptime(DateTime.Now - st)}" : "꺼져 있음";
        TextRenderer.DrawText(g, $"{players}   ·   {uptime}", Theme.Font(8.75f), new Rectangle(18, 112, Width - 36, 18), Theme.TextMuted, flags);
    }
}
