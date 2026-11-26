using GameServerKeeper.Core.Games;

namespace GameServerKeeper.UI;

/// <summary>
/// PropertyGrid 기반 범용 편집 창 (서버 프로필 / 전역 설정 공용).
/// 호출 측이 복제본을 넘기므로 [취소] 시 원본은 바뀌지 않는다.
/// </summary>
public sealed class PropertyEditForm : Form
{
    private readonly Func<List<string>> _validate;

    public PropertyEditForm(string title, object target, Func<List<string>> validate, string? note = null)
    {
        _validate = validate;

        Text = title;
        Font = new Font("Malgun Gothic", 9f);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 720);
        MinimumSize = new Size(480, 400);
        MinimizeBox = false;
        ShowInTaskbar = false;

        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = target,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true
        };
        Load += (_, _) => grid.ExpandAllGridItems();

        var ok = new Button { Text = "저장", Width = 90, Height = 30 };
        var cancel = new Button { Text = "취소", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => OnSave();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);

        Controls.Add(grid);
        if (note != null)
        {
            Controls.Add(new Label
            {
                Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 4, 8, 4), ForeColor = Color.DimGray, Text = note
            });
        }
        Controls.Add(bar);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnSave()
    {
        var errors = _validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors.Select(e => "• " + e)), "설정 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>서버 추가: 게임 종류 + 이름 선택</summary>
public sealed class AddServerDialog : Form
{
    private readonly ComboBox _cboGame = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 100, Top = 14, Width = 260 };
    private readonly TextBox _txtName = new() { Left = 100, Top = 48, Width = 260 };

    public GameDefinition? SelectedGame => _cboGame.SelectedItem as GameDefinition;
    public string ServerName => _txtName.Text.Trim();

    public AddServerDialog()
    {
        Text = "서버 추가";
        Font = new Font("Malgun Gothic", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 130);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        foreach (var g in GameRegistry.All) _cboGame.Items.Add(g);
        _cboGame.SelectedIndex = 0;
        _cboGame.SelectedIndexChanged += (_, _) =>
        {
            if (SelectedGame != null) _txtName.Text = SelectedGame.CreateDefaultProfile().Name;
        };
        _txtName.Text = GameRegistry.All[0].CreateDefaultProfile().Name;

        var ok = new Button { Text = "다음 ▶", DialogResult = DialogResult.OK, Left = 204, Top = 88, Width = 75 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Left = 285, Top = 88, Width = 75 };

        Controls.AddRange([
            new Label { Text = "게임", Left = 14, Top = 18, AutoSize = true }, _cboGame,
            new Label { Text = "서버 이름", Left = 14, Top = 52, AutoSize = true }, _txtName,
            ok, cancel
        ]);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
