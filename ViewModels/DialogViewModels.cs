using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Services;

namespace GameServerKeeper.ViewModels;

/// <summary>
/// 서버 추가 1단계: 이름 + 서버 실행 파일. 게임 종류는 실행 파일 이름으로 자동 인식한다.
/// (인식 결과가 틀리면 다음 단계 설정 화면의 "게임 종류"에서 바꿀 수 있음)
/// </summary>
public sealed partial class AddServerViewModel : DialogViewModel
{
    private const string DefaultRoot = @"D:\Steam_Dedicated_Server";
    private readonly IDialogService _dialogs;
    private bool _nameTouched;
    private bool _settingName;

    public AddServerViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Detect();
    }

    public GameDefinition DetectedGame { get; private set; } = GameRegistry.Generic;
    public string CleanExePath => ExePath.Trim().Trim('"');

    [ObservableProperty] private string _exePath = "";
    [ObservableProperty] private string _serverName = "새 서버";
    [ObservableProperty] private string _detectTitle = "";
    [ObservableProperty] private string _detectDescription = "";
    [ObservableProperty] private string _detectGlyph = Glyphs.Info;
    [ObservableProperty] private bool _isDetected;

    partial void OnExePathChanged(string value) => Detect();

    partial void OnServerNameChanged(string value)
    {
        if (!_settingName) _nameTouched = true;
    }

    private void Detect()
    {
        var path = CleanExePath;
        DetectedGame = GameRegistry.Detect(path);
        bool generic = ReferenceEquals(DetectedGame, GameRegistry.Generic);
        IsDetected = !string.IsNullOrWhiteSpace(path) && !generic;

        if (string.IsNullOrWhiteSpace(path))
            (DetectTitle, DetectDescription, DetectGlyph) = ("실행 파일을 골라 주세요", "고른 파일을 보고 게임을 자동으로 알아내요. 모르는 게임은 '범용'으로 추가돼요.", Glyphs.Info);
        else if (generic)
            (DetectTitle, DetectDescription, DetectGlyph) = ("알 수 없는 게임 → 범용으로 추가해요", DetectedGame.Description, Glyphs.Server);
        else
            (DetectTitle, DetectDescription, DetectGlyph) = ($"{DetectedGame.DisplayName} 서버로 인식했어요", DetectedGame.Description, Glyphs.Game);

        // 이름을 직접 바꾸지 않았다면 게임에 맞는 이름 제안
        if (!_nameTouched && !string.IsNullOrWhiteSpace(path))
        {
            _settingName = true;
            try { ServerName = DetectedGame.CreateProfile(path).Name; } catch { /* 잘못된 경로 */ }
            _settingName = false;
        }
    }

    [RelayCommand]
    private void Browse()
    {
        string? initial = CleanExePath.Length > 0 ? CleanExePath : Directory.Exists(DefaultRoot) ? DefaultRoot : null;
        var picked = _dialogs.PickFile("서버 실행 파일 선택", "실행 파일|*.exe;*.bat;*.cmd|모든 파일|*.*", initial);
        if (picked != null) ExePath = picked;
    }

    [RelayCommand]
    private void Next() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>한 줄 입력</summary>
public sealed partial class PromptViewModel(string title, string caption, string okText) : DialogViewModel
{
    public string Title { get; } = title;
    public string Caption { get; } = caption;
    public string OkText { get; } = okText;

    [ObservableProperty] private string _text = "";

    [RelayCommand]
    private void Ok() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>선택지 대화상자 (MessageBox 대신)</summary>
public sealed partial class ChoiceViewModel : DialogViewModel
{
    public sealed class Item(ChoiceViewModel owner, int index, DialogChoice choice)
    {
        public string Label => choice.Label;
        public string? Description => choice.Description;
        public ChoiceKind Kind => choice.Kind;
        public IRelayCommand Command { get; } = new RelayCommand(() => owner.Select(index));
    }

    public ChoiceViewModel(string title, string message, IReadOnlyList<DialogChoice> choices)
    {
        Title = title;
        Message = message;
        Choices = choices.Select((c, i) => new Item(this, i, c)).ToList();
    }

    public string Title { get; }
    public string Message { get; }
    public IReadOnlyList<Item> Choices { get; }

    /// <summary>고른 순번 (창을 그냥 닫으면 -1)</summary>
    public int Result { get; private set; } = -1;

    private void Select(int index)
    {
        Result = index;
        Close(true);
    }
}
