using System.ComponentModel;
using System.Text.Json.Serialization;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Games.Generic;
using GameServerKeeper.Games.Palworld;

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
    /// <summary><see cref="GameSettings"/> 파생 타입</summary>
    public required Type SettingsType { get; init; }
    public required Func<IAppLog, IGameServerAdapter> CreateAdapter { get; init; }
    /// <summary>"서버 추가" 시 채워질 기본 프로필</summary>
    public required Func<ServerProfile> CreateDefaultProfile { get; init; }

    public override string ToString() => DisplayName;
}

/// <summary>
/// 지원 게임 목록.
/// ▶ 새 게임 추가 방법: Games/{게임}/ 폴더에 Settings + Adapter + Definition 을 만들고 아래 목록에 한 줄 추가.
/// </summary>
public static class GameRegistry
{
    private static readonly List<GameDefinition> Games =
    [
        PalworldGame.Definition,
        GenericGame.Definition,
        // ValheimGame.Definition,   ← 예: 새 게임은 여기에 추가
    ];

    public static IReadOnlyList<GameDefinition> All => Games;

    public static GameDefinition? Find(string gameId) =>
        Games.FirstOrDefault(g => string.Equals(g.GameId, gameId, StringComparison.OrdinalIgnoreCase));

    public static GameDefinition Get(string gameId) =>
        Find(gameId) ?? throw new InvalidOperationException($"등록되지 않은 게임: {gameId}");
}
