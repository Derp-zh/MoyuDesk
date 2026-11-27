using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal sealed record ReleaseInfo(
    string TagName,
    string Version,
    string Title,
    string NotesSummary,
    string HtmlUrl,
    string? ZipAssetName,
    string? ZipDownloadUrl,
    string? ChecksumsDownloadUrl);

internal sealed record UpdateCheckResult(
    bool Success,
    bool UpdateAvailable,
    string CurrentVersion,
    string Message,
    ReleaseInfo? Release = null);

// Zero-server open-source updater backed directly by GitHub Releases API.
// Downloads portable ZIP + SHA256SUMS.txt into %LOCALAPPDATA%\MoyuDesk\updates\,
// verifies cryptographic integrity, extracts, and hands off to scripts/install.ps1
// for atomic fixed-directory replacement + rollback backup + restart.
internal static class AppUpdater
{
    internal const string LatestReleaseApiUrl = "https://api.github.com/repos/Derp-zh/MoyuDesk/releases/latest";
    internal const string ReleasesPageUrl = "https://github.com/Derp-zh/MoyuDesk/releases";

    internal static string CurrentVersion =>
        typeof(AppUpdater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static string UpdatesCacheRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoyuDesk", "updates");

    internal static string DefaultInstallDirectory => StartupLinks.FixedInstallDirectory;

    internal static bool TryParseSemVer(string? raw, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0) text = text[..dash];
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 4) return false;
        if (!int.TryParse(parts[0], out var major) || major < 0) return false;
        if (!int.TryParse(parts[1], out var minor) || minor < 0) return false;
        var build = 0;
        var revision = 0;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out build) || build < 0)) return false;
        if (parts.Length >= 4 && (!int.TryParse(parts[3], out revision) || revision < 0)) return false;
        version = new Version(major, minor, build, revision);
        return true;
    }

    internal static int CompareVersions(string? left, string? right)
    {
        var okL = TryParseSemVer(left, out var vl);
        var okR = TryParseSemVer(right, out var vr);
        if (!okL && !okR) return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        if (!okL) return -1;
        if (!okR) return 1;
        return vl.CompareTo(vr);
    }

    internal static bool IsNewerVersion(string currentVersion, string candidateVersion) =>
        CompareVersions(currentVersion, candidateVersion) < 0;

    internal static ReleaseInfo ParseLatestReleaseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;

        var version = TryParseSemVer(tag, out var parsed)
            ? (parsed.Revision > 0 ? parsed.ToString(4) : parsed.ToString(3))
            : tag.TrimStart('v', 'V');

        string? zipName = null;
        string? zipUrl = null;
        string? sumsUrl = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                var downloadUrl = asset.TryGetProperty("browser_download_url", out var du) ? du.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(downloadUrl)) continue;

                if (assetName.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
                    (sumsUrl is null && assetName.Contains("SHA256", StringComparison.OrdinalIgnoreCase) && assetName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    sumsUrl = downloadUrl;
                    continue;
                }

                if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var isPreferred = assetName.Contains("MoyuDesk", StringComparison.OrdinalIgnoreCase)
                        && assetName.Contains("portable", StringComparison.OrdinalIgnoreCase)
                        && assetName.Contains("win-x64", StringComparison.OrdinalIgnoreCase);
                    if (isPreferred || zipUrl is null)
                    {
                        zipName = assetName;
                        zipUrl = downloadUrl;
                    }
                }
            }
        }

        return new ReleaseInfo(
            TagName: tag,
            Version: version,
            Title: string.IsNullOrWhiteSpace(name) ? tag : name,
            NotesSummary: SummarizeNotes(body),
            HtmlUrl: string.IsNullOrWhiteSpace(htmlUrl) ? ReleasesPageUrl : htmlUrl,
            ZipAssetName: zipName,
            ZipDownloadUrl: zipUrl,
            ChecksumsDownloadUrl: sumsUrl);
    }

    internal static string SummarizeNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "本次更新包含功能改进与问题修复。";
        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("```", StringComparison.Ordinal) && !l.StartsWith("---", StringComparison.Ordinal))
            .Select(l => l.TrimStart('#', '*', '-', ' ', '\t'))
            .Where(l => l.Length > 0)
            .Take(6)
            .ToArray();
        var joined = string.Join("\n", lines);
        return joined.Length > 360 ? joined[..360].TrimEnd() + "…" : joined;
    }

    internal static string? ParseExpectedSha256(string sha256SumsContent, string assetFileName)
    {
        if (string.IsNullOrWhiteSpace(sha256SumsContent) || string.IsNullOrWhiteSpace(assetFileName)) return null;
        var targetName = Path.GetFileName(assetFileName.Trim());
        var lines = sha256SumsContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length < 65 || line.StartsWith('#')) continue;
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var hash = parts[0].Trim();
            if (!IsHex64(hash)) continue;
            var filePart = parts[^1].Trim().TrimStart('*');
            if (string.Equals(Path.GetFileName(filePart), targetName, StringComparison.OrdinalIgnoreCase))
                return hash.ToUpperInvariant();
        }
        return null;
    }

    private static bool IsHex64(string value)
    {
        if (value.Length != 64) return false;
        foreach (var ch in value)
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')))
                return false;
        return true;
    }

    internal static bool VerifyFileSha256(string filePath, string expectedHex, out string actualHex)
    {
        using var stream = File.OpenRead(filePath);
        actualHex = Convert.ToHexString(SHA256.HashData(stream));
        return IsHex64(expectedHex) && string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttpClient(string currentVersion)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MoyuDesk/{currentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal static async Task<UpdateCheckResult> CheckForUpdateAsync(string? currentVersion = null, HttpClient? httpClient = null, CancellationToken ct = default)
    {
        var current = string.IsNullOrWhiteSpace(currentVersion) ? CurrentVersion : currentVersion;
        var ownedClient = httpClient is null;
        var client = httpClient ?? CreateHttpClient(current);
        try
        {
            using var response = await client.GetAsync(LatestReleaseApiUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, false, current,
                    $"检查更新失败（GitHub 返回 {(int)response.StatusCode}），可稍后重试或点击打开 GitHub 发布页查看。");
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            var release = ParseLatestReleaseJson(json);
            if (string.IsNullOrWhiteSpace(release.Version))
                return new UpdateCheckResult(false, false, current, "未能识别远端版本号，请打开 GitHub 发布页查看。");

            if (IsNewerVersion(current, release.Version))
            {
                return new UpdateCheckResult(true, true, current,
                    $"发现新版本 {release.Version}（当前 {current}）", release);
            }
            return new UpdateCheckResult(true, false, current,
                $"当前已是最新版本（{current}）", release);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(false, false, current, "检查更新超时，请检查网络连接或稍后重试。");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, current, "检查更新失败：" + ex.Message);
        }
        finally
        {
            if (ownedClient) client.Dispose();
        }
    }

    internal static void ExtractZipSafely(string zipPath, string destinationDir)
    {
        if (Directory.Exists(destinationDir)) Directory.Delete(destinationDir, true);
        Directory.CreateDirectory(destinationDir);
        var fullRoot = Path.GetFullPath(destinationDir) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destinationDir, entry.FullName));
            if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新压缩包包含非法路径：" + entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    internal static string? FindInstallScript(string extractedRoot)
    {
        var direct = Path.Combine(extractedRoot, "scripts", "install.ps1");
        if (File.Exists(direct)) return direct;
        foreach (var sub in Directory.GetDirectories(extractedRoot))
        {
            var nested = Path.Combine(sub, "scripts", "install.ps1");
            if (File.Exists(nested)) return nested;
        }
        return null;
    }

    internal static async Task<(bool Ok, string Message)> DownloadVerifyAndLaunchInstallAsync(
        ReleaseInfo release,
        Action<string>? onProgress = null,
        string? updatesRoot = null,
        string? installDir = null,
        bool launchInstaller = true,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(release.ZipDownloadUrl) || string.IsNullOrWhiteSpace(release.ZipAssetName))
            return (false, "该发布版本未附带 Windows 便携安装包，请打开 GitHub 发布页手动下载。");
        if (string.IsNullOrWhiteSpace(release.ChecksumsDownloadUrl))
            return (false, "该发布版本缺少 SHA256SUMS.txt 校验文件，为保障安全已中止自动升级。");
        if (!Uri.TryCreate(release.ZipDownloadUrl, UriKind.Absolute, out var zipUri) || zipUri.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(release.ChecksumsDownloadUrl, UriKind.Absolute, out var sumUri) || sumUri.Scheme != Uri.UriSchemeHttps)
            return (false, "更新下载地址不是受信任的 HTTPS 链接，已中止升级。");

        var root = updatesRoot ?? UpdatesCacheRoot;
        var targetInstallDir = installDir ?? DefaultInstallDirectory;
        var safeVersion = string.Concat(release.Version.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var workDir = Path.Combine(root, string.IsNullOrWhiteSpace(safeVersion) ? "latest" : safeVersion);
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, Path.GetFileName(release.ZipAssetName));
        var sumsPath = Path.Combine(workDir, "SHA256SUMS.txt");
        var extractDir = Path.Combine(workDir, "extracted");

        var ownedClient = httpClient is null;
        var client = httpClient ?? CreateHttpClient(CurrentVersion);
        try
        {
            onProgress?.Invoke("正在下载校验文件 SHA256SUMS.txt…");
            var sumsText = await client.GetStringAsync(sumUri, ct);
            await File.WriteAllTextAsync(sumsPath, sumsText, new UTF8Encoding(false), ct);
            var expectedHash = ParseExpectedSha256(sumsText, release.ZipAssetName);
            if (expectedHash is null)
                return (false, $"SHA256SUMS.txt 中未找到 {release.ZipAssetName} 的校验值，已中止升级。");

            onProgress?.Invoke($"正在下载 {release.ZipAssetName}…");
            using (var resp = await client.GetAsync(zipUri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var net = await resp.Content.ReadAsStreamAsync(ct);
                await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await net.CopyToAsync(file, ct);
            }

            onProgress?.Invoke("正在校验安装包完整性（SHA256）…");
            if (!VerifyFileSha256(zipPath, expectedHash, out var actualHash))
            {
                try { File.Delete(zipPath); } catch { }
                return (false, $"安装包 SHA256 校验未通过（期望 {expectedHash[..12]}…，实际 {actualHash[..12]}…），已删除文件并中止升级。");
            }

            onProgress?.Invoke("正在解压新版本安装包…");
            ExtractZipSafely(zipPath, extractDir);

            var installScript = FindInstallScript(extractDir);
            if (installScript is null)
                return (false, "新版本压缩包缺少 scripts\\install.ps1，请打开 GitHub 发布页手动安装。");

            if (!launchInstaller)
                return (true, $"已通过 SHA256 校验并解压至 {extractDir}");

            onProgress?.Invoke("校验通过，正在启动升级安装程序并重启…");
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(installScript)!
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(installScript);
            psi.ArgumentList.Add("-InstallDir");
            psi.ArgumentList.Add(targetInstallDir);
            Process.Start(psi);
            return (true, "升级程序已启动，将在几秒内自动备份旧版、替换并重启新版摸鱼台。");
        }
        catch (Exception ex)
        {
            return (false, "下载或升级失败：" + ex.Message);
        }
        finally
        {
            if (ownedClient) client.Dispose();
        }
    }
}
