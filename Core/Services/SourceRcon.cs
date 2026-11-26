using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace GameServerKeeper.Core.Services;

/// <summary>
/// Source RCON 프로토콜 클라이언트 (https://developer.valvesoftware.com/wiki/Source_RCON_Protocol).
/// V Rising, ARK, Minecraft 등 여러 게임이 같은 방식을 쓰므로 공용으로 둔다.
/// 명령 하나마다 연결 → 인증 → 실행 → 종료 (서버 재시작에도 연결 상태를 신경 쓸 필요 없음)
/// </summary>
public static class SourceRcon
{
    private const int TypeResponse = 0;
    private const int TypeExecCommand = 2;
    private const int TypeAuthResponse = 2;
    private const int TypeAuth = 3;

    /// <summary>명령 실행 결과</summary>
    public sealed record Result(bool Success, string Response, string? Error);

    public static async Task<Result> ExecuteAsync(string host, int port, string password, string command,
        TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();

            // 1) 인증
            await WriteAsync(stream, 1, TypeAuth, password, cts.Token).ConfigureAwait(false);
            while (true)
            {
                var (id, type, _) = await ReadAsync(stream, cts.Token).ConfigureAwait(false);
                if (type != TypeAuthResponse) continue; // 일부 서버는 빈 RESPONSE_VALUE 를 먼저 보냄
                if (id == -1) return new Result(false, "", "RCON 인증 실패 (비밀번호 확인)");
                break;
            }

            // 2) 명령 실행
            await WriteAsync(stream, 2, TypeExecCommand, command, cts.Token).ConfigureAwait(false);
            var (_, _, body) = await ReadAsync(stream, cts.Token).ConfigureAwait(false);
            return new Result(true, body, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Result(false, "", $"RCON 응답 시간 초과 ({timeout.TotalSeconds:0}초)");
        }
        catch (SocketException ex)
        {
            return new Result(false, "", $"RCON 연결 실패 ({host}:{port}) — {ex.SocketErrorCode}");
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            return new Result(false, "", $"RCON 연결이 끊어졌어요 — {ex.Message}");
        }
    }

    private static async Task WriteAsync(NetworkStream stream, int id, int type, string body, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var packet = new byte[4 + 4 + 4 + payload.Length + 2];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0), packet.Length - 4);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
        payload.CopyTo(packet, 12);
        // 마지막 2바이트는 0 (본문 종료 + 빈 문자열)
        await stream.WriteAsync(packet, ct).ConfigureAwait(false);
    }

    private static async Task<(int Id, int Type, string Body)> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size < 10 || size > 1024 * 1024) throw new IOException($"잘못된 RCON 패킷 크기: {size}");

        var data = new byte[size];
        await stream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        int id = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0));
        int type = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        var body = Encoding.UTF8.GetString(data, 8, Math.Max(0, size - 10));
        return (id, type, body);
    }
}
