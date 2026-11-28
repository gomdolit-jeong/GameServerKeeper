using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Services;

public enum ChoiceKind { Primary, Danger, Secondary }

/// <summary>선택 대화상자의 버튼 하나</summary>
public sealed record DialogChoice(string Label, string? Description = null, ChoiceKind Kind = ChoiceKind.Secondary);

/// <summary>
/// ViewModel 이 창(Window)을 직접 만들지 않도록 대화상자를 대신 띄워 주는 서비스.
/// 구현(DialogService)만 WPF 를 알고, ViewModel 은 이 인터페이스만 쓴다.
/// </summary>
public interface IDialogService
{
    /// <summary>선택지 중 하나를 고르게 한다. 고른 순번, 창을 닫으면 -1.</summary>
    int Choose(string title, string message, IReadOnlyList<DialogChoice> choices);

    void Alert(string title, string message);

    /// <summary>한 줄 입력. 취소하면 null.</summary>
    string? Prompt(string title, string caption, string okText);

    bool ShowSettings(SettingsViewModel vm);

    bool ShowAddServer(AddServerViewModel vm);

    /// <param name="initialPath">처음 열 폴더 또는 파일 경로 (없으면 null)</param>
    string? PickFile(string title, string filter, string? initialPath);

    string? PickFolder(string title, string? initialPath);
}

/// <summary>탐색기·클립보드처럼 OS 에 의존하는 기능</summary>
public interface IShellService
{
    void OpenFolder(string path);
    void SetClipboard(string text);
}
