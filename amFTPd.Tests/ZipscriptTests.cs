using System;
using System.Collections.Generic;
using System.Text;
using FluentFTP;
using Xunit;

namespace amFTPd.Tests;

[Collection("AMScriptTests")] // Reuse the collection for sequential execution if needed, though Zipscript uses its own dirs
public class ZipscriptTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public ZipscriptTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UploadSfvAndFiles_DetectsCompletion()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        var releaseName = "TEST.RELEASE-ZLS";
        var releasePath = $"/0DAY/{releaseName}";
        await client.CreateDirectory(releasePath);

        // 1. Create fake files
        var file1Data = Encoding.UTF8.GetBytes("File 1 content");
        var file2Data = Encoding.UTF8.GetBytes("File 2 content");

        var crc1 = amFTPd.Utils.Cryptography.Crc32.Compute(file1Data);
        var crc2 = amFTPd.Utils.Cryptography.Crc32.Compute(file2Data);

        var crc1Hex = crc1.ToString("X8");
        var crc2Hex = crc2.ToString("X8");

        // 2. Create SFV content
        var sfvContent = $"; SFV created by test\r\ntest-file-1.zip {crc1Hex}\r\ntest-file-2.zip {crc2Hex}\r\n";
        var sfvData = Encoding.UTF8.GetBytes(sfvContent);

        // 3. Upload SFV
        await client.UploadBytes(sfvData, $"{releasePath}/test.sfv", FtpRemoteExists.Overwrite);

        // 4. Upload File 1
        await client.UploadBytes(file1Data, $"{releasePath}/test-file-1.zip", FtpRemoteExists.Overwrite);

        // 5. Upload File 2
        await client.UploadBytes(file2Data, $"{releasePath}/test-file-2.zip", FtpRemoteExists.Overwrite);

        await WaitForReleaseToReachStateAsync(
            client,
            releasePath,
            TimeSpan.FromSeconds(5),
            summary => summary.HasSfv && summary.Complete,
            "release did not become SFV-complete");

        var sfv = await WaitForSfvFileStatesAsync(
            client,
            releasePath,
            TimeSpan.FromSeconds(5));

        Assert.Equal("OK", sfv["test-file-1.zip"]);
        Assert.Equal("OK", sfv["test-file-2.zip"]);
        Assert.False(sfv.ContainsKey("test.sfv"));

        // We can't easily check the internal state of ZipscriptEngine here without exposing it,
        // but we can check if it generated any logs or if we can query it via a SITE command if implemented.
        // For now, let's verify that the files exist and the server didn't crash.
        Assert.True(await client.FileExists($"{releasePath}/test-file-1.zip"));
        Assert.True(await client.FileExists($"{releasePath}/test-file-2.zip"));
    }

    [Fact]
    public async Task UploadCorruptFile_ShouldBeLogged()
    {
        using var client = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass);

        var releaseName = "CORRUPT.RELEASE-ZLS";
        var releasePath = $"/0DAY/{releaseName}";
        await client.CreateDirectory(releasePath);

        // 1. Create SFV with a specific CRC
        var sfvContent = "corrupt-file.zip 12345678\r\n";
        await client.UploadBytes(Encoding.UTF8.GetBytes(sfvContent), $"{releasePath}/test.sfv", FtpRemoteExists.Overwrite);

        // 2. Upload file with DIFFERENT content (CRC won't be 12345678)
        var fileData = Encoding.UTF8.GetBytes("This is not the content you are looking for");
        await client.UploadBytes(fileData, $"{releasePath}/corrupt-file.zip", FtpRemoteExists.Overwrite);

        await WaitForReleaseToReachStateAsync(
            client,
            releasePath,
            TimeSpan.FromSeconds(5),
            summary => summary.HasSfv && summary.Bad > 0,
            "release did not report bad CRC");

        var sfv = await WaitForSfvFileStatesAsync(
            client,
            releasePath,
            TimeSpan.FromSeconds(5));

        Assert.Equal("BADCRC", sfv["corrupt-file.zip"]);

        Assert.True(await client.FileExists($"{releasePath}/corrupt-file.zip"));
    }

    private async Task<ZipscriptSummary> WaitForReleaseToReachStateAsync(
        FluentFTP.AsyncFtpClient client,
        string releasePath,
        TimeSpan timeout,
        Func<ZipscriptSummary, bool> predicate,
        string failureMessage)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        string lastMessage = string.Empty;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var reply = await client.Execute($"SITE RSCHECK {releasePath}");
            var responseText = MergeCommandOutput(reply);
            if (!reply.Success)
            {
                lastMessage = responseText;
                await Task.Delay(100);
                continue;
            }

            var parsed = ParseRscCheck(responseText);
            lastMessage = responseText;
            if (predicate(parsed))
                return parsed;

            await Task.Delay(100);
        }

        throw new TimeoutException($"{failureMessage}. Last response: {lastMessage}");
    }

    private async Task<Dictionary<string, string>> WaitForSfvFileStatesAsync(
        FluentFTP.AsyncFtpClient client,
        string releasePath,
        TimeSpan timeout)
    {
        Dictionary<string, string>? last = null;
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var reply = await client.Execute($"SITE SFV {releasePath}");
            var responseText = MergeCommandOutput(reply);
            if (reply.Success)
            {
                last = ParseSfvFileStates(responseText);
                if (last.Count > 0)
                    return last;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"timed out waiting for SITE SFV output: {last?.Count ?? 0} files");
    }

    private static ZipscriptSummary ParseRscCheck(string message)
    {
        var summary = new ZipscriptSummary(
            HasSfv: false,
            Complete: false,
            Total: 0,
            Bad: 0,
            Missing: 0);

        foreach (var line in message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("Has SFV:", StringComparison.OrdinalIgnoreCase))
                summary = summary with { HasSfv = trimmed.Contains("YES", StringComparison.OrdinalIgnoreCase) };
            if (trimmed.Contains("Complete:", StringComparison.OrdinalIgnoreCase))
                summary = summary with { Complete = trimmed.Contains("YES", StringComparison.OrdinalIgnoreCase) };
            if (!trimmed.StartsWith("211- Files:", StringComparison.OrdinalIgnoreCase))
                continue;

            var payload = trimmed["211- Files:".Length..].Trim();
            foreach (var segment in payload.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = segment.Split('=');
                if (parts.Length != 2)
                    continue;
                if (string.Equals(parts[0], "total", StringComparison.OrdinalIgnoreCase))
                    summary = summary with { Total = int.Parse(parts[1]) };
                else if (string.Equals(parts[0], "bad", StringComparison.OrdinalIgnoreCase))
                    summary = summary with { Bad = int.Parse(parts[1]) };
                else if (string.Equals(parts[0], "missing", StringComparison.OrdinalIgnoreCase))
                    summary = summary with { Missing = int.Parse(parts[1]) };
            }
        }

        return summary;
    }

    private static Dictionary<string, string> ParseSfvFileStates(string message)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("211-", StringComparison.OrdinalIgnoreCase) ||
                !trimmed.Contains("exp=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed.StartsWith("211- Has SFV:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("211- Complete:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("211- Files:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("211- Zipscript status", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = trimmed[4..].Trim();
            var tokens = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            result[tokens[1]] = tokens[0];
        }

        return result;
    }

    private static string MergeCommandOutput(FluentFTP.FtpReply reply)
    {
        var lines = new List<string>(capacity: 8);
        if (!string.IsNullOrWhiteSpace(reply.Message))
            lines.Add(reply.Message);

        if (reply.InfoMessages is { Length: > 0 })
            lines.AddRange(reply.InfoMessages);

        return string.Join("\r\n", lines);
    }

    private sealed record ZipscriptSummary(
        bool HasSfv,
        bool Complete,
        int Total,
        int Bad,
        int Missing);
}
