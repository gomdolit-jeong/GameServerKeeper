using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Games.Palworld;

public sealed class PalworldPlayerDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("accountName")] public string? AccountName { get; set; }
    [JsonPropertyName("playerId")] public string? PlayerId { get; set; }
    [JsonPropertyName("userId")] public string? UserId { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
    [JsonPropertyName("ping")] public double Ping { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
}

public sealed class PalworldPlayersResponse
{
    [JsonPropertyName("players")] public List<PalworldPlayerDto> Players { get; set; } = new();
}

/// <summary>
/// 팰월드 내장 REST API 클라이언트 (http://host:port/v1/api/..., Basic Auth admin:{AdminPassword}).
/// PalWorldSettings.ini 에 RESTAPIEnabled=True 가 필요하다.
/// 모든 메서드는 예외를 던지지 않고 실패 시 null/false 를 반환한다.
/// </summary>
public sealed class PalworldRestClient : IDisposable
{
    private const string Src = "REST";
    private readonly IAppLog _log;
    private HttpClient _http;

    public PalworldRestClient(IAppLog log)
    {
        _log = log;
        _http = CreateClient("127.0.0.1", 8212, "");
    }

    /// <summary>HttpClient 는 BaseAddress 를 바꿀 수 없어서 설정 변경 시 새로 만든다.</summary>
    public void Configure(string host, int port, string adminPassword)
    {
        var old = _http;
        _http = CreateClient(host, port, adminPassword);
        old.Dispose();
    }

    private static HttpClient CreateClient(string host, int port, string password)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}:{port}/v1/api/"),
            Timeout = Timeout.InfiniteTimeSpan // 호출별 타임아웃으로 제어
        };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<List<PalworldPlayerDto>?> GetPlayersAsync(CancellationToken ct)
    {
        var res = await GetAsync<PalworldPlayersResponse>("players", TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        return res?.Players;
    }

    public Task<bool> AnnounceAsync(string message, CancellationToken ct) =>
        PostAsync("announce", new { message }, TimeSpan.FromSeconds(10), ct);

    /// <summary>월드가 크면 오래 걸릴 수 있어 타임아웃을 넉넉히 둔다.</summary>
    public Task<bool> SaveAsync(CancellationToken ct) => PostAsync("save", null, TimeSpan.FromSeconds(120), ct);

    public Task<bool> ShutdownAsync(int waitSeconds, string message, CancellationToken ct) =>
        PostAsync("shutdown", new { waittime = waitSeconds, message }, TimeSpan.FromSeconds(15), ct);

    private async Task<T?> GetAsync<T>(string path, TimeSpan timeout, CancellationToken ct) where T : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var resp = await _http.GetAsync(path, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    _log.Warn(Src, $"GET {path} 인증 실패(401) — AdminPassword 를 확인하세요.");
                else
                    _log.Debug(Src, $"GET {path} 실패: {(int)resp.StatusCode}");
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 서버가 아직 안 떴거나 꺼진 경우 — 스팸 방지를 위해 Debug 로만
            _log.Debug(Src, $"GET {path} 오류: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> PostAsync(string path, object? body, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            HttpContent content = body == null
                ? new StringContent("", Encoding.UTF8, "application/json")
                : JsonContent.Create(body);
            using (content)
            using (var resp = await _http.PostAsync(path, content, cts.Token).ConfigureAwait(false))
            {
                if (resp.IsSuccessStatusCode) return true;
                var text = await resp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                _log.Warn(Src, $"POST {path} 실패: {(int)resp.StatusCode} {resp.ReasonPhrase} {(text.Length > 200 ? text[..200] + "…" : text)}");
                return false;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warn(Src, $"POST {path} 시간 초과 ({timeout.TotalSeconds:0}초)");
            return false;
        }
        catch (Exception ex)
        {
            _log.Warn(Src, $"POST {path} 오류: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
