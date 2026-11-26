using System.Net.Http.Json;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Core.Services;

/// <summary>디스코드 웹훅 알림. URL 이 비어 있으면 아무것도 하지 않는다. 실패해도 예외를 던지지 않는다.</summary>
public sealed class DiscordNotifier
{
    private const string Src = "Discord";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IAppLog _log;
    private readonly Func<string> _serverName;

    public DiscordNotifier(IAppLog log, Func<string> serverName)
    {
        _log = log;
        _serverName = serverName;
    }

    public string? WebhookUrl { get; set; }

    public async Task SendAsync(string message)
    {
        var url = WebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            var payload = new
            {
                username = "GameServerKeeper",
                content = $"**[{_serverName()}]** {message}\n-# {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };
            using var resp = await Http.PostAsJsonAsync(url, payload).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                _log.Warn(Src, $"웹훅 전송 실패: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            _log.Warn(Src, $"웹훅 전송 오류: {ex.Message}");
        }
    }
}
