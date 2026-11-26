using System.ComponentModel;
using System.Text.Json.Serialization;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Games.CoreKeeper;
using GameServerKeeper.Games.Generic;
using GameServerKeeper.Games.Palworld;
using GameServerKeeper.Games.VRising;

namespace GameServerKeeper.Core.Games;

/// <summary>
/// 게임 전용 설정의 기반 클래스. config.json 에는 "gameId" 판별자와 함께 저장된다.
/// PropertyGrid 에서 펼쳐서 편집할 수 있도록 ExpandableObjectConverter 를 사용한다.
/// </summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public abstract class GameSettings
{
    [Browsable(false), JsonIgnore]
    public abstract string GameId { get; }

    public virtual IEnumerable<string> Validate() => [];

    public override string ToString() => GameRegistry.Find(GameId)?.DisplayName ?? GameId;
}

/// <summary>게임 하나를 등록하기 위한 정의</summary>
public sealed class GameDefinition
{
    /// <summary>config.json 판별자. 한 번 정하면 바꾸지 말 것.</summary>
    public required string GameId { get; init; }
    public required string DisplayName { get; init; }
    /// <summary>"서버 추가" 화면의 게임 설명</summary>
    public string Description { get; init; } = "";
    /// <summary>서버 설정 화면 상단 안내 문구</summary>
    public string SettingsNote { get; init; } = "";
    /// <summary><see cref="GameSettings"/> 파생 타입</summary>
    public required Type SettingsType { get; init; }
    public required Func<IAppLog, IGameServerAdapter> CreateAdapter { get; init; }

    /// <summary>
    /// 자동 감지용 실행 파일 이름 (예: "PalServer.exe").
    /// "서버 추가"에서 사용자가 고른 실행 파일 이름이 여기에 있으면 이 게임으로 인식한다.
    /// </summary>
    public string[] ExeFileNames { get; init; } = [];

    /// <summary>게임 종류를 이 게임으로 바꿀 때 App ID 가 비어 있으면 채울 값 (스팀 게임이 아니면 0)</summary>
    public int DefaultAppId { get; init; }

    /// <summary>
    /// 기본 프로필 생성. exePath 가 있으면 그 위치를 기준으로 설치·세이브·백업 폴더 등을 채운다.
    /// (null 이면 게임의 일반적인 기본값)
    /// </summary>
    public required Func<string?, ServerProfile> CreateProfile { get; init; }

    public bool MatchesExe(string exePath)
    {
        var name = Path.GetFileName(exePath.Trim().Trim('"'));
        return ExeFileNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// 지원 게임 목록.
/// ▶ 새 게임 추가 방법: Games/{게임}/ 폴더에 Settings + Adapter + Definition(ExeFileNames, CreateProfile 포함)을
///   만들고 아래 목록에 한 줄 추가. 범용(Generic)은 자동 감지 실패 시 기본값이므로 항상 마지막에 둔다.
/// </summary>
public static class GameRegistry
{
    private static readonly List<GameDefinition> Games =
    [
        PalworldGame.Definition,
        VRisingGame.Definition,
        CoreKeeperGame.Definition,
        GenericGame.Definition,
    ];

    public static IReadOnlyList<GameDefinition> All => Games;

    public static GameDefinition Generic => GenericGame.Definition;

    /// <summary>실행 파일로 게임 자동 감지. 모르는 파일이면 범용.</summary>
    public static GameDefinition Detect(string? exePath) =>
        string.IsNullOrWhiteSpace(exePath)
            ? Generic
            : Games.FirstOrDefault(g => g.MatchesExe(exePath)) ?? Generic;

    public static GameDefinition? Find(string gameId) =>
        Games.FirstOrDefault(g => string.Equals(g.GameId, gameId, StringComparison.OrdinalIgnoreCase));

    public static GameDefinition Get(string gameId) =>
        Find(gameId) ?? throw new InvalidOperationException($"등록되지 않은 게임: {gameId}");
}
