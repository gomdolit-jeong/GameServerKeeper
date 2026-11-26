using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Core.Server;

/// <summary>
/// 서버 인스턴스 목록 관리 (추가/편집/삭제) + config.json 동기화.
/// UI 스레드에서만 호출하는 것을 전제로 한다.
/// </summary>
public sealed class ServerManager : IDisposable
{
    private const string Src = "Manager";
    private readonly Logger _log;
    private readonly List<ServerInstance> _servers = new();
    private AppConfig _config;
    private bool _initialized;

    /// <summary>서버 추가/삭제/이름 변경 시</summary>
    public event Action? ServersChanged;

    public ServerManager(AppConfig config, Logger log)
    {
        _config = config;
        _log = log;
        foreach (var profile in config.Servers)
            _servers.Add(new ServerInstance(profile, config, log));

        foreach (var err in AppConfig.ValidateServerSet(config.Servers))
            _log.Warn(Src, $"설정 충돌: {err}");
    }

    public AppConfig Config => _config;
    public IReadOnlyList<ServerInstance> Servers => _servers;
    public IEnumerable<ServerInstance> RunningServers => _servers.Where(s => s.IsServerRunning);

    public ServerInstance? Find(string id) => _servers.FirstOrDefault(s => s.Id == id);

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        foreach (var s in _servers) s.Initialize();
    }

    /// <summary>새 프로필 검증 (서버 간 충돌 포함). 오류 목록 반환.</summary>
    public List<string> ValidateProfile(ServerProfile candidate)
    {
        var errors = candidate.Validate();
        var set = _config.Servers.Where(s => s.Id != candidate.Id).Append(candidate).ToList();
        errors.AddRange(AppConfig.ValidateServerSet(set));
        return errors;
    }

    public ServerInstance AddServer(ServerProfile profile)
    {
        _config.Servers.Add(profile);
        Save();
        var instance = new ServerInstance(profile, _config, _log);
        _servers.Add(instance);
        if (_initialized) instance.Initialize();
        _log.Info(Src, $"서버 추가: {profile.Name} ({instance.Game.DisplayName})");
        ServersChanged?.Invoke();
        return instance;
    }

    public void UpdateServer(ServerProfile updated)
    {
        var idx = _config.Servers.FindIndex(s => s.Id == updated.Id);
        var instance = Find(updated.Id);
        if (idx < 0 || instance == null) throw new InvalidOperationException("존재하지 않는 서버입니다.");

        bool gameChanged = !string.Equals(instance.Game.GameId, updated.GameId, StringComparison.OrdinalIgnoreCase);
        if (gameChanged && (instance.IsServerRunning || instance.IsBusy))
            throw new InvalidOperationException("게임 종류를 바꾸려면 서버를 먼저 종료하세요.");

        _config.Servers[idx] = updated;
        Save();

        if (gameChanged)
        {
            // 게임 어댑터는 인스턴스에 묶여 있으므로 같은 Id 로 새로 만든다
            int pos = _servers.IndexOf(instance);
            instance.Dispose();
            var replacement = new ServerInstance(updated, _config, _log);
            _servers[pos] = replacement;
            if (_initialized) replacement.Initialize();
            _log.Info(Src, $"게임 종류 변경: {updated.Name} → {replacement.Game.DisplayName}");
        }
        else
        {
            instance.ApplySettings(updated, _config);
        }
        ServersChanged?.Invoke();
    }

    /// <summary>실행 중이거나 작업 중인 서버는 삭제할 수 없다.</summary>
    public bool RemoveServer(string id, out string? reason)
    {
        reason = null;
        var instance = Find(id);
        if (instance == null) { reason = "존재하지 않는 서버입니다."; return false; }
        if (instance.IsServerRunning || instance.IsBusy) { reason = "서버를 먼저 종료하세요."; return false; }

        instance.Dispose();
        _servers.Remove(instance);
        _config.Servers.RemoveAll(s => s.Id == id);
        Save();
        _log.Info(Src, $"서버 삭제: {instance.Name} (백업 파일은 삭제하지 않음)");
        ServersChanged?.Invoke();
        return true;
    }

    /// <summary>전역 설정 변경 (서버 목록은 유지)</summary>
    public void UpdateGlobal(AppConfig newGlobal)
    {
        newGlobal.Servers = _config.Servers;
        _config = newGlobal;
        Save();
        foreach (var s in _servers) s.ApplySettings(s.Profile, _config);
    }

    public void Save()
    {
        try { ConfigManager.Save(_config); }
        catch (Exception ex) { _log.Error(Src, "config.json 저장 실패", ex); throw; }
    }

    /// <summary>실행 중인 모든 서버를 병렬로 안전 종료</summary>
    public Task StopAllAsync(string reason) =>
        Task.WhenAll(RunningServers.ToList().Select(s => s.StopServerAsync(countdown: true, reason)));

    public void Dispose()
    {
        foreach (var s in _servers) s.Dispose();
        _servers.Clear();
    }
}
