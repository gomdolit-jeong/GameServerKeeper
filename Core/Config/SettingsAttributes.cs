using GameServerKeeper.Core.Games;

namespace GameServerKeeper.Core.Config;

public enum PathKind { Folder, File }

/// <summary>설정 화면에서 "찾아보기" 버튼을 붙일 경로 항목</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PathEditorAttribute(PathKind kind, string filter = "모든 파일|*.*") : Attribute
{
    public PathKind Kind { get; } = kind;
    /// <summary>OpenFileDialog 필터 (예: "실행 파일|*.exe;*.bat")</summary>
    public string Filter { get; } = filter;
}

/// <summary>
/// 게임이 이 기능을 지원할 때만 설정 화면에 보이는 항목.
/// (예: 인게임 공지를 못 하는 게임에는 카운트다운 항목을 숨김)
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiresCapabilityAttribute(GameCapabilities capability) : Attribute
{
    public GameCapabilities Capability { get; } = capability;
}
