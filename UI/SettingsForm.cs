using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;

namespace GameServerKeeper.UI;

/// <summary>
/// 설정 화면. 설정 클래스의 특성(Category/DisplayName/Description/Range/PathEditor/RequiresCapability)을 읽어
/// 항목별 편집기를 자동으로 만든다. → 새 게임의 전용 설정도 클래스만 만들면 화면이 자동 생성됨.
/// 호출 측이 복제본을 넘기므로 [취소] 시 원본은 바뀌지 않는다.
/// </summary>
public sealed class SettingsForm : Form
{
    private sealed record Entry(object Owner, PropertyInfo Prop, string Category, string Display, string? Description,
        GameCapabilities? Requires, bool IsGameSelector = false);
    private sealed record EnumItem(object Value, string Text) { public override string ToString() => Text; }

    private readonly object _target;
    private readonly Func<string> _subtitle;
    private readonly bool _allowGameChange;
    private readonly Func<List<string>> _validate;
    private readonly Func<GameCapabilities>? _capabilities;
    private Label _subtitleLabel = null!;
    private readonly ToolTip _tip = Theme.CreateToolTip();

    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };
    private readonly TableLayoutPanel _grid = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2, Padding = new Padding(32, 0, 32, 32), BackColor = Theme.Surface
    };
    private readonly Panel _nav = new() { Dock = DockStyle.Left, Width = 210, BackColor = Theme.SurfaceAlt, Padding = new Padding(0, 14, 0, 0) };
    private readonly Panel _errorBanner = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.DangerSoft, Padding = new Padding(32, 12, 32, 12), Visible = false };
    private readonly Label _errorText = new() { AutoSize = true, ForeColor = Theme.Danger, Font = Theme.Font(9.5f), BackColor = Theme.DangerSoft };

    private readonly List<Label> _wrapLabels = new();
    private readonly List<(GameCapabilities Req, string Category, Control[] Controls)> _capRows = new();
    private readonly Dictionary<string, Label> _sectionHints = new();
    private readonly List<(NavItem Nav, Control Header)> _sections = new();
    private int _row;

    /// <param name="subtitle">제목 아래 안내 문구 (게임 종류를 바꾸면 다시 계산)</param>
    /// <param name="allowGameChange">서버 프로필 편집 시 게임 종류 변경 허용 여부 (실행 중인 서버는 false)</param>
    public SettingsForm(string title, Func<string> subtitle, object target, Func<List<string>> validate,
        Func<GameCapabilities>? capabilities = null, bool allowGameChange = true)
    {
        _target = target;
        _subtitle = subtitle;
        _allowGameChange = allowGameChange;
        _validate = validate;
        _capabilities = capabilities;

        Text = title;
        Icon = AppIcon.Get();
        Font = Theme.Font(9.5f);
        BackColor = Theme.Surface;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 760);
        MinimumSize = new Size(760, 520);
        MinimizeBox = false;
        ShowInTaskbar = false;

        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        _scroll.Controls.Add(_grid);
        _errorBanner.Controls.Add(_errorText);

        BuildRows(target);
        var header = BuildHeader(title, subtitle());
        var footer = BuildFooter();

        // 도킹 순서: 나중에 추가한 것이 바깥쪽
        Controls.Add(_scroll);
        Controls.Add(_errorBanner);
        Controls.Add(_nav);
        Controls.Add(footer);
        Controls.Add(header);

        _scroll.Resize += (_, _) => UpdateWrapWidths();
        Load += (_, _) => { UpdateWrapWidths(); RefreshCapabilities(); };
    }

    // ═════════════ 화면 구성 ═════════════

    private Control BuildHeader(string title, string subtitle)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Surface };
        var icon = new PictureBox { Image = AppIcon.Render(40), Size = new Size(40, 40), Location = new Point(28, 22), SizeMode = PictureBoxSizeMode.Zoom };
        var t = new Label { Text = title, Font = Theme.Font(15f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(80, 16) };
        var s = _subtitleLabel = new Label { Text = subtitle, Font = Theme.Font(9.5f), ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(82, 50) };
        header.Controls.AddRange([icon, t, s]);
        header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        return header;
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Theme.Surface };
        footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });

        var tip = new Label
        {
            Text = "항목 이름에 마우스를 올리면 자세한 설명이 나와요.",
            ForeColor = Theme.TextMuted, Font = Theme.Font(9f), AutoSize = true, Location = new Point(28, 26)
        };

        var save = new ModernButton("저장", Glyphs.Save, ButtonVariant.Primary);
        var cancel = new ModernButton("취소", null, ButtonVariant.Secondary) { DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => OnSave();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 16, 20, 0), BackColor = Theme.Surface, WrapContents = false
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        cancel.Margin = new Padding(8, 0, 0, 0);
        save.Margin = Padding.Empty;

        footer.Controls.Add(buttons);
        footer.Controls.Add(tip);
        AcceptButton = save;
        CancelButton = cancel;
        return footer;
    }

    private void BuildRows(object target)
    {
        string? current = null;
        foreach (var e in Enumerate(target, null))
        {
            if (e.Category != current)
            {
                AddSection(e.Category);
                current = e.Category;
            }
            AddRow(e);
        }
    }

    /// <summary>게임 종류가 바뀌면 게임 전용 항목이 달라지므로 화면 전체를 다시 만든다 (값은 대상 객체에 있어서 유지됨)</summary>
    private void Rebuild()
    {
        var scrollY = -_scroll.AutoScrollPosition.Y;
        _grid.SuspendLayout();
        _nav.SuspendLayout();
        foreach (var c in _grid.Controls.Cast<Control>().ToList()) c.Dispose();
        foreach (var c in _nav.Controls.Cast<Control>().ToList()) c.Dispose();
        _grid.Controls.Clear();
        _grid.RowStyles.Clear();
        _grid.RowCount = _row = 0;
        _wrapLabels.Clear();
        _capRows.Clear();
        _sectionHints.Clear();
        _sections.Clear();

        BuildRows(_target);
        _nav.ResumeLayout();
        _grid.ResumeLayout();
        _subtitleLabel.Text = _subtitle();
        UpdateWrapWidths();
        RefreshCapabilities();
        _scroll.AutoScrollPosition = new Point(0, scrollY);
    }

    private IEnumerable<Entry> Enumerate(object owner, string? forcedCategory)
    {
        foreach (var p in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false) continue;
            var category = forcedCategory ?? p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "기타";

            // 게임 전용 설정은 펼쳐서 같은 화면에 표시
            if (typeof(GameSettings).IsAssignableFrom(p.PropertyType))
            {
                // 서버 프로필이면 맨 위에 "게임 종류" 선택 항목
                if (owner is ServerProfile)
                    yield return new Entry(owner, p, category, "게임 종류",
                        _allowGameChange
                            ? "이 서버가 어떤 게임인지예요. 게임마다 지원하는 기능(공지·저장·접속자 조회)과 아래 전용 항목이 달라져요. 실행 파일로 자동 인식했지만 틀렸다면 여기서 바꾸세요."
                            : "이 서버가 어떤 게임인지예요. 서버가 실행 중일 때는 바꿀 수 없어요. 먼저 서버를 종료하세요.",
                        null, IsGameSelector: true);

                if (p.GetValue(owner) is { } nested)
                    foreach (var inner in Enumerate(nested, category)) yield return inner;
                continue;
            }
            if (!p.CanWrite || p.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly == true) continue;

            yield return new Entry(owner, p, category,
                p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name,
                p.GetCustomAttribute<DescriptionAttribute>()?.Description,
                p.GetCustomAttribute<RequiresCapabilityAttribute>()?.Capability);
        }
    }

    private int NextRow()
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.RowCount = ++_row;
        return _row - 1;
    }

    private void AddSection(string category)
    {
        var header = new Label
        {
            Text = category,
            Font = Theme.Font(12.5f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true,
            Margin = new Padding(0, 30, 0, 6)
        };
        int r = NextRow();
        _grid.Controls.Add(header, 0, r);
        _grid.SetColumnSpan(header, 2);

        var hint = new Label
        {
            AutoSize = true, ForeColor = Theme.Info, BackColor = Theme.InfoSoft, Font = Theme.Font(9f),
            Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 0, 6), Visible = false
        };
        r = NextRow();
        _grid.Controls.Add(hint, 0, r);
        _grid.SetColumnSpan(hint, 2);
        _sectionHints[category] = hint;

        var nav = new NavItem(category, null, dark: false) { Dock = DockStyle.Top };
        nav.Click += (_, _) =>
        {
            foreach (var (n, _) in _sections) n.Selected = ReferenceEquals(n, nav);
            _scroll.AutoScrollPosition = new Point(0, Math.Max(0, header.Top - 10));
        };
        if (_sections.Count == 0) nav.Selected = true;
        _sections.Add((nav, header));
        _nav.Controls.Add(nav);
        nav.BringToFront(); // Dock=Top 은 나중에 추가한 것이 위로 가므로 순서 보정
    }

    private void AddRow(Entry e)
    {
        int r = NextRow();

        var labelBox = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 14, 28, 14), BackColor = Theme.Surface
        };
        var title = new Label { Text = e.Display, Font = Theme.Font(9.75f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Margin = Padding.Empty };
        labelBox.Controls.Add(title);
        _wrapLabels.Add(title);
        if (!string.IsNullOrWhiteSpace(e.Description))
        {
            var desc = new Label { Text = e.Description, Font = Theme.Font(8.75f), ForeColor = Theme.TextMuted, AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
            labelBox.Controls.Add(desc);
            _wrapLabels.Add(desc);
            _tip.SetToolTip(desc, Theme.WrapTip(e.Description));
        }

        var editor = CreateEditor(e);
        editor.Margin = new Padding(0, 14, 0, 14);

        _grid.Controls.Add(labelBox, 0, r);
        _grid.Controls.Add(editor, 1, r);

        var sep = new Panel { Height = 1, BackColor = Theme.Border, Margin = Padding.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        int r2 = NextRow();
        _grid.Controls.Add(sep, 0, r2);
        _grid.SetColumnSpan(sep, 2);

        if (!string.IsNullOrWhiteSpace(e.Description))
        {
            var tipText = Theme.WrapTip($"{e.Display}\n{e.Description}");
            _tip.SetToolTip(title, tipText);
            SetTipRecursive(editor, tipText);
        }

        if (e.Requires is GameCapabilities req)
            _capRows.Add((req, e.Category, [labelBox, editor, sep]));
    }

    private void SetTipRecursive(Control c, string text)
    {
        _tip.SetToolTip(c, text);
        foreach (Control child in c.Controls) SetTipRecursive(child, text);
    }

    // ═════════════ 편집기 ═════════════

    private Control CreateEditor(Entry e)
    {
        if (e.IsGameSelector) return CreateGameSelector((ServerProfile)e.Owner);

        var p = e.Prop;
        var type = p.PropertyType;
        var value = p.GetValue(e.Owner);

        void Set(object? v)
        {
            p.SetValue(e.Owner, v);
            if (e.Owner is GameSettings) RefreshCapabilities(); // 게임 설정이 바뀌면 지원 기능이 달라질 수 있음
        }

        if (type == typeof(bool))
        {
            var toggle = new ToggleSwitch { Checked = (bool)value!, Anchor = AnchorStyles.Left };
            toggle.CheckedChanged += (_, _) => Set(toggle.Checked);
            return toggle;
        }

        if (type == typeof(int) || type == typeof(double))
        {
            var range = p.GetCustomAttribute<RangeAttribute>();
            bool isDouble = type == typeof(double);
            var num = new NumericUpDown
            {
                Font = Theme.Font(10f), Width = 160, Anchor = AnchorStyles.Left, BorderStyle = BorderStyle.FixedSingle,
                DecimalPlaces = isDouble ? 1 : 0, Increment = isDouble ? 0.5m : 1m, ThousandsSeparator = false,
                Minimum = range != null ? Convert.ToDecimal(range.Minimum) : 0,
                Maximum = range != null ? Convert.ToDecimal(range.Maximum) : int.MaxValue
            };
            num.Value = Math.Clamp(Convert.ToDecimal(value), num.Minimum, num.Maximum);
            num.ValueChanged += (_, _) => Set(isDouble ? (double)num.Value : (object)(int)num.Value);
            return num;
        }

        if (type.IsEnum)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Font(10f), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            foreach (var v in Enum.GetValues(type))
            {
                var desc = type.GetField(v.ToString()!)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? v.ToString()!;
                var item = new EnumItem(v, desc);
                combo.Items.Add(item);
                if (Equals(v, value)) combo.SelectedItem = item;
            }
            combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is EnumItem it) Set(it.Value); };
            return combo;
        }

        // 문자열
        var pathAttr = p.GetCustomAttribute<PathEditorAttribute>();
        bool password = p.GetCustomAttribute<PasswordPropertyTextAttribute>()?.Password == true;
        var text = new TextBox
        {
            Text = value as string ?? "", Font = Theme.Font(10f), BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = password, Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        text.TextChanged += (_, _) => Set(text.Text);
        if (pathAttr == null && !password) return text;

        var wrap = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, BackColor = Theme.Surface
        };
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        text.Margin = new Padding(0, 3, 0, 0);

        ModernButton button;
        if (password)
        {
            button = new ModernButton("", Glyphs.View) { Compact = true };
            _tip.SetToolTip(button, "비밀번호 보기 / 숨기기");
            button.Click += (_, _) => text.UseSystemPasswordChar = !text.UseSystemPasswordChar;
        }
        else
        {
            button = new ModernButton("찾아보기", Glyphs.Folder) { Compact = true };
            button.Click += (_, _) => Browse(pathAttr!, e.Display, text);
        }
        button.Margin = new Padding(8, 0, 0, 0);

        wrap.Controls.Add(text, 0, 0);
        wrap.Controls.Add(button, 1, 0);
        return wrap;
    }

    private Control CreateGameSelector(ServerProfile profile)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Font(10f), Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Enabled = _allowGameChange
        };
        foreach (var g in GameRegistry.All)
        {
            combo.Items.Add(g);
            if (string.Equals(g.GameId, profile.GameId, StringComparison.OrdinalIgnoreCase)) combo.SelectedItem = g;
        }
        combo.SelectionChangeCommitted += (_, _) =>
        {
            if (combo.SelectedItem is not GameDefinition next || next.GameId == profile.GameId) return;
            var prev = GameRegistry.Find(profile.GameId);
            profile.Game = (GameSettings)Activator.CreateInstance(next.SettingsType)!;
            // App ID 가 비어 있거나 이전 게임의 기본값 그대로면 새 게임 기본값으로
            if (profile.AppId == 0 || profile.AppId == prev?.DefaultAppId) profile.AppId = next.DefaultAppId;
            BeginInvoke(Rebuild); // 이 콤보박스 자신도 다시 만들어지므로 이벤트가 끝난 뒤 실행
        };
        return combo;
    }

    private void Browse(PathEditorAttribute attr, string title, TextBox target)
    {
        if (attr.Kind == PathKind.Folder)
        {
            using var dlg = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, ShowNewFolderButton = true };
            if (Directory.Exists(target.Text)) dlg.InitialDirectory = target.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
        }
        else
        {
            using var dlg = new OpenFileDialog { Title = title, Filter = attr.Filter, CheckFileExists = false };
            try
            {
                var dir = Path.GetDirectoryName(target.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                dlg.FileName = Path.GetFileName(target.Text);
            }
            catch { /* 잘못된 경로 문자열 무시 */ }
            if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.FileName;
        }
    }

    // ═════════════ 동작 ═════════════

    private void UpdateWrapWidths()
    {
        int col = (int)((_scroll.ClientSize.Width - _grid.Padding.Horizontal) * 0.48) - 30;
        if (col < 120) return;
        foreach (var l in _wrapLabels) l.MaximumSize = new Size(col, 0);
        _errorText.MaximumSize = new Size(Math.Max(200, _errorBanner.ClientSize.Width - 64), 0);
    }

    /// <summary>게임이 지원하지 않는 기능의 항목은 숨기고, 섹션 위에 안내 문구 표시</summary>
    private void RefreshCapabilities()
    {
        if (_capabilities == null) return;
        GameCapabilities caps;
        try { caps = _capabilities(); }
        catch { return; }

        var hidden = new Dictionary<string, int>();
        _grid.SuspendLayout();
        foreach (var (req, category, controls) in _capRows)
        {
            bool show = (caps & req) == req;
            foreach (var c in controls) c.Visible = show;
            if (!show) hidden[category] = hidden.GetValueOrDefault(category) + 1;
        }
        foreach (var (category, hint) in _sectionHints)
        {
            int n = hidden.GetValueOrDefault(category);
            hint.Visible = n > 0;
            if (n > 0) hint.Text = $"ⓘ  이 게임은 인게임 공지나 접속자 조회를 지원하지 않아서 관련 항목 {n}개를 숨겼어요.";
        }
        _grid.ResumeLayout();
    }

    private void OnSave()
    {
        var errors = _validate();
        if (errors.Count > 0)
        {
            _errorText.Text = "저장하지 못했어요. 아래 내용을 확인해 주세요.\n" + string.Join("\n", errors.Select(x => "•  " + x));
            _errorBanner.Visible = true;
            UpdateWrapWidths();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
