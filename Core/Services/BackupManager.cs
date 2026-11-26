using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Core.Services;

/// <summary>
/// 세이브 폴더를 {접두어}_yyyyMMdd_HHmmss.zip 으로 압축하고, 같은 접두어의 오래된 백업만 정리한다.
/// (팰월드 기본 접두어 "Palworld_Backup" → Palworld_Backup_20261126_040000.zip)
/// </summary>
public sealed class BackupManager
{
    private const string Src = "Backup";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";
    private readonly IAppLog _log;

    public BackupManager(IAppLog log) => _log = log;

    public DateTime? LastBackupTime { get; private set; }

    /// <summary>
    /// 백업 zip 생성. 성공 시 zip 경로, 실패 시 null.
    /// 파일을 FileShare.ReadWrite 로 읽으므로 서버 실행 중 "핫 백업"도 가능하지만,
    /// 일관성을 위해 정기 백업은 서버가 완전히 종료된 상태에서 수행한다.
    /// </summary>
    public async Task<string?> CreateBackupAsync(string saveDir, string backupDir, string prefix, CancellationToken ct = default)
    {
        if (!Directory.Exists(saveDir))
        {
            _log.Error(Src, $"세이브 폴더가 없습니다: {saveDir}");
            return null;
        }

        Directory.CreateDirectory(backupDir);
        var now = DateTime.Now;
        var finalPath = Path.Combine(backupDir, $"{prefix}_{now.ToString(TimestampFormat, CultureInfo.InvariantCulture)}.zip");
        var tmpPath = finalPath + ".tmp"; // 압축 중인 파일이 정상 백업으로 오인되지 않도록

        _log.Info(Src, $"백업 시작: {saveDir}");
        var sw = Stopwatch.StartNew();
        try
        {
            var (files, skipped) = await Task.Run(() => CreateZip(saveDir, tmpPath, ct), ct).ConfigureAwait(false);
            File.Move(tmpPath, finalPath, overwrite: true);

            var sizeMb = new FileInfo(finalPath).Length / 1024.0 / 1024.0;
            _log.Info(Src, $"백업 완료: {Path.GetFileName(finalPath)} ({files}개 파일, {sizeMb:0.0} MB, {sw.Elapsed.TotalSeconds:0.0}초)" +
                           (skipped > 0 ? $" — 읽지 못한 파일 {skipped}개" : ""));
            LastBackupTime = now;
            return finalPath;
        }
        catch (Exception ex)
        {
            _log.Error(Src, "백업 실패", ex);
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* 무시 */ }
            return null;
        }
    }

    private (int Files, int Skipped) CreateZip(string sourceDir, string zipPath, CancellationToken ct)
    {
        var root = Path.GetFullPath(sourceDir);
        var baseName = new DirectoryInfo(root).Name; // zip 안에 "Saved/..." 처럼 들어감 → 그대로 덮어쓰기 복원 가능
        int files = 0, skipped = 0;

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            try
            {
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var entry = zip.CreateEntry($"{baseName}/{rel}", CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(file);
                using var output = entry.Open();
                input.CopyTo(output);
                files++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                _log.Warn(Src, $"파일 건너뜀: {rel} ({ex.Message})");
            }
        }
        return (files, skipped);
    }

    /// <summary>보관 일수가 지난 백업(같은 접두어만) 삭제. 삭제한 개수 반환.</summary>
    public int CleanupOldBackups(string backupDir, string prefix, int retentionDays)
    {
        if (retentionDays <= 0 || !Directory.Exists(backupDir)) return 0;

        var regex = NameRegex(prefix);
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        int deleted = 0;

        foreach (var path in Directory.EnumerateFiles(backupDir, prefix + "_*.zip"))
        {
            var name = Path.GetFileName(path);
            var m = regex.Match(name);
            if (!m.Success) continue; // 접두어가 비슷한 다른 서버 백업은 건드리지 않음

            var created = DateTime.TryParseExact(m.Groups[1].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var ts) ? ts : File.GetLastWriteTime(path);
            if (created >= cutoff) continue;

            try
            {
                File.Delete(path);
                deleted++;
                _log.Info(Src, $"오래된 백업 삭제: {name}");
            }
            catch (Exception ex)
            {
                _log.Warn(Src, $"백업 삭제 실패: {name} ({ex.Message})");
            }
        }

        // 비정상 종료로 남은 .tmp 정리
        foreach (var tmp in Directory.EnumerateFiles(backupDir, prefix + "_*.zip.tmp"))
        {
            try { if (File.GetLastWriteTime(tmp) < DateTime.Now.AddHours(-1)) File.Delete(tmp); } catch { /* 무시 */ }
        }

        if (deleted > 0) _log.Info(Src, $"보관 기간({retentionDays}일) 지난 백업 {deleted}개 삭제");
        return deleted;
    }

    public void LoadLastBackupTime(string backupDir, string prefix)
    {
        if (!Directory.Exists(backupDir)) return;
        var regex = NameRegex(prefix);
        var latest = Directory.EnumerateFiles(backupDir, prefix + "_*.zip")
            .Where(p => regex.IsMatch(Path.GetFileName(p)))
            .Select(File.GetLastWriteTime)
            .DefaultIfEmpty()
            .Max();
        if (latest != default) LastBackupTime = latest;
    }

    private static Regex NameRegex(string prefix) =>
        new($"^{Regex.Escape(prefix)}_(\\d{{8}}_\\d{{6}})\\.zip$", RegexOptions.IgnoreCase);
}
