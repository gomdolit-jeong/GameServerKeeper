using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Core.Games;

/// <summary>게임별로 지원하는 기능. 지원하지 않는 기능은 공통 로직이 자동으로 건너뛴다.</summary>
[Flags]
public enum GameCapabilities
{
    None = 0,
    /// <summary>접속자 목록 조회 가능 → 접속자 0명이면 카운트다운 생략</summary>
    PlayerList = 1,
    /// <summary>인게임 공지 가능 → 종료 카운트다운 공지</summary>
    Announce = 2,
    /// <summary>월드 강제 저장 가능</summary>
    Save = 4,
    /// <summary>정상 종료 요청 가능 (불가능하면 바로 Kill)</summary>
    GracefulShutdown = 8,
}

/// <summary>게임 중립 접속자 정보</summary>
public sealed record PlayerInfo(string Id, string Name, int? Level = null, double? Ping = null);

/// <summary>
/// 게임별 차이를 흡수하는 어댑터.
/// 공통 로직(ServerInstance)은 프로세스 실행/감시, 업데이트, 백업, 스케줄, 크래시 복구, 메모리 감시를 담당하고,
/// 어댑터는 "이 게임에서 저장/공지/종료/접속자 조회를 어떻게 하는가"만 구현한다.
/// </summary>
public interface IGameServerAdapter : IDisposable
{
    GameCapabilities Capabilities { get; }

    /// <summary>
    /// 메모리 측정/크래시 감시 대상인 실제 서버 프로세스 이름 (확장자 제외).
    /// 런처가 자식 프로세스를 띄우는 게임(팰월드 등)은 자식 이름을 반환한다.
    /// </summary>
    string TrackedProcessName { get; }

    /// <summary>프로필(설정) 적용. 생성 직후와 설정 변경 시 호출된다.</summary>
    void Configure(ServerProfile profile);

    /// <summary>접속자 목록. 조회 실패(서버 응답 없음) 시 null.</summary>
    Task<IReadOnlyList<PlayerInfo>?> GetPlayersAsync(CancellationToken ct);

    Task<bool> AnnounceAsync(string message, CancellationToken ct);

    Task<bool> SaveAsync(CancellationToken ct);

    /// <summary>정상 종료 요청. 요청이 받아들여졌으면 true (실제 종료는 공통 로직이 대기/Kill 처리).</summary>
    Task<bool> RequestShutdownAsync(int serverPid, CancellationToken ct);

    /// <summary>시작 시 게임 설정 파일 점검. 경고 메시지 목록 반환.</summary>
    IEnumerable<string> CheckConfiguration();
}

/// <summary>기본 구현: 전부 "지원 안 함". 게임 어댑터는 필요한 것만 override 한다.</summary>
public abstract class GameServerAdapterBase : IGameServerAdapter
{
    protected GameServerAdapterBase(IAppLog log) => Log = log;

    protected IAppLog Log { get; }
    protected ServerProfile Profile { get; private set; } = null!;

    public abstract GameCapabilities Capabilities { get; }

    public virtual string TrackedProcessName => Path.GetFileNameWithoutExtension(Profile.ServerExePath);

    public virtual void Configure(ServerProfile profile) => Profile = profile;

    public virtual Task<IReadOnlyList<PlayerInfo>?> GetPlayersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PlayerInfo>?>(null);

    public virtual Task<bool> AnnounceAsync(string message, CancellationToken ct) => Task.FromResult(false);

    public virtual Task<bool> SaveAsync(CancellationToken ct) => Task.FromResult(false);

    public virtual Task<bool> RequestShutdownAsync(int serverPid, CancellationToken ct) => Task.FromResult(false);

    public virtual IEnumerable<string> CheckConfiguration() => [];

    public virtual void Dispose() { }
}
