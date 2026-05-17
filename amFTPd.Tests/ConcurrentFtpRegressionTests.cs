using System.Net.Sockets;
using System.Text;
using amFTPd.Core.Zipscript;
using amFTPd.Utils.Cryptography;

namespace amFTPd.Tests;

public sealed class ConcurrentFtpRegressionTests : IClassFixture<FtpTestFixture>
{
    private readonly FtpTestFixture _fixture;

    public ConcurrentFtpRegressionTests(FtpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentUploads_ToSameRelease_RecordEveryRacerExactlyOnce()
    {
        var releasePath = $"/0DAY/RACE.CONCURRENT.{Guid.NewGuid():N}-ZLS";
        var racers = new List<(string UserName, string Password)>
        {
            (_fixture.GAdminUser, _fixture.GAdminPass),
            (_fixture.NormalUser, _fixture.NormalPass)
        };

        racers.AddRange(_fixture.RaceAccounts.Select(a => (a.UserName, a.Password)));

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await admin.CreateDirectory(releasePath);
        }

        var expectedBytes = 0L;
        var tasks = new List<Task>();
        for (var i = 0; i < racers.Count; i++)
        {
            var index = i;
            var racer = racers[index];
            var payload = Encoding.ASCII.GetBytes(new string((char)('A' + index), 4096 + index));
            expectedBytes += payload.Length;

            tasks.Add(Task.Run(async () =>
            {
                await using var client = await RawFtpControlSession.ConnectAsync(_fixture.Port);
                await client.LoginAsync(racer.UserName, racer.Password);
                var remotePath = $"{releasePath}/{racer.UserName}.bin";
                await client.UploadPassiveAsync(remotePath, payload);
            }));
        }

        await Task.WhenAll(tasks);

        Assert.True(
            _fixture.Runtime.RaceEngine.TryGetRace(releasePath, out var race),
            "Concurrent uploads did not create a race entry for the release.");
        Assert.Equal(racers.Count, race.FileCount);
        Assert.Equal(expectedBytes, race.TotalBytes);

        foreach (var racer in racers)
        {
            Assert.True(
                race.UserBytes.TryGetValue(racer.UserName, out var bytes),
                $"Race entry is missing uploader {racer.UserName}.");
            Assert.True(bytes > 0, $"Race entry recorded zero bytes for {racer.UserName}.");
        }
    }

    [Fact]
    public async Task StoraOverwrite_UpdatesDupeTotalBytesToLatestSize()
    {
        var releaseName = $"RESUME.OVR.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/SAFE/{releaseName}";
        var file = $"{releasePath}/file001.bin";
        var firstPayload = Encoding.ASCII.GetBytes("ONE");
        var secondPayload = Encoding.ASCII.GetBytes("ONE-TWO-THREE");
        Directory.CreateDirectory(Path.Combine(_fixture.RootPath, "SAFE", releaseName));

        await using (var initial = await RawFtpControlSession.ConnectAsync(_fixture.Port))
        {
            await initial.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
            await initial.UploadPassiveAsync(file, firstPayload);
        }

        {
            var interim = Assert.IsType<amFTPd.Core.Dupe.BinaryDupeStore>(_fixture.Runtime.DupeStore);
            var interimEntries = interim.GetAll()
                .Where(e => string.Equals(e.SectionName, "SAFE", StringComparison.OrdinalIgnoreCase))
                .Select(e => $"{e.ReleaseName}:{e.TotalBytes}")
                .ToList();
        }

        await using (var overwrite = await RawFtpControlSession.ConnectAsync(_fixture.Port))
        {
            await overwrite.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
            var response = await overwrite.TryUploadPassiveAsync(file, secondPayload);
            Assert.StartsWith("226 ", response, StringComparison.Ordinal);
        }

        var dupe = Assert.IsType<amFTPd.Core.Dupe.BinaryDupeStore>(_fixture.Runtime.DupeStore);
        var entry = dupe.Find("SAFE", releaseName);
        Assert.NotNull(entry);
        Assert.Equal(secondPayload.Length, entry.TotalBytes);
    }

    [Fact]
    public async Task RestStorResume_UpdatesDupeTotalBytesUsingFinalFileState()
    {
        var releaseName = $"RESUME.CRC.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/SAFE/{releaseName}";
        var file = $"{releasePath}/file001.bin";
        var partial = Encoding.ASCII.GetBytes("GOOD");
        var append = Encoding.ASCII.GetBytes("REST");
        var partialPath = Path.Combine(_fixture.RootPath, "SAFE", releaseName, "file001.bin");

        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, partial);
        await File.WriteAllTextAsync(partialPath + ".partcrc", amFTPd.Utils.Cryptography.Crc32.ToHex(amFTPd.Utils.Cryptography.Crc32.Compute(partial)));

        await using var uploader = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await uploader.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

        var rest = await uploader.CommandAsync($"REST {partial.Length}");
        Assert.StartsWith("350 ", rest, StringComparison.Ordinal);

        var stor = await uploader.TryUploadPassiveAsync(file, append);
        Assert.StartsWith("226 ", stor, StringComparison.Ordinal);

        var dupe = Assert.IsType<amFTPd.Core.Dupe.BinaryDupeStore>(_fixture.Runtime.DupeStore);
        var allEntries = dupe.GetAll()
            .Where(e => string.Equals(e.SectionName, "SAFE", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"{e.ReleaseName}:{e.TotalBytes}")
            .ToList();
        var entry = dupe.Find("SAFE", releaseName);
        Assert.NotNull(entry);
        Assert.Equal(partial.Length + append.Length, entry.TotalBytes);
    }

    [Fact]
    public async Task RestStorResume_WithBadPartialCrc_IsRejectedBeforeDataTransfer()
    {
        var releaseName = $"RESUME.BADCRC.{Guid.NewGuid():N}-ZLS";
        var physicalReleasePath = Path.Combine(_fixture.RootPath, "SAFE", releaseName);
        Directory.CreateDirectory(physicalReleasePath);

        var partialPath = Path.Combine(physicalReleasePath, "file001.bin");
        await File.WriteAllBytesAsync(partialPath, Encoding.ASCII.GetBytes("GOOD"));

        var wrongCrc = Crc32.ToHex(Crc32.Compute(Encoding.ASCII.GetBytes("BAD!")));
        await File.WriteAllTextAsync(partialPath + ".partcrc", wrongCrc);

        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

        var rest = await ftp.CommandAsync("REST 4");
        Assert.StartsWith("350 ", rest, StringComparison.Ordinal);

        var stor = await ftp.CommandAsync($"/SAFE/{releaseName}/file001.bin", "STOR");
        Assert.StartsWith("550 Resume integrity check failed:", stor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestStorResume_WithOffsetPastPartialSize_IsRejected()
    {
        var releaseName = $"RESUME.OVERSIZE.{Guid.NewGuid():N}-ZLS";
        var physicalReleasePath = Path.Combine(_fixture.RootPath, "SAFE", releaseName);
        Directory.CreateDirectory(physicalReleasePath);

        var partialPath = Path.Combine(physicalReleasePath, "file001.bin");
        await File.WriteAllBytesAsync(partialPath, Encoding.ASCII.GetBytes("GOOD"));

        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

        var rest = await ftp.CommandAsync("REST 999");
        Assert.StartsWith("350 ", rest, StringComparison.Ordinal);

        var stor = await ftp.CommandAsync($"/SAFE/{releaseName}/file001.bin", "STOR");
        Assert.StartsWith("550 Resume integrity check failed:", stor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestStorResume_WithNoPartialFile_IsRejected()
    {
        var releaseName = $"RESUME.MISSING.{Guid.NewGuid():N}-ZLS";
        var physicalReleasePath = Path.Combine(_fixture.RootPath, "SAFE", releaseName);
        Directory.CreateDirectory(physicalReleasePath);

        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

        var rest = await ftp.CommandAsync("REST 32");
        Assert.StartsWith("350 ", rest, StringComparison.Ordinal);

        var stor = await ftp.CommandAsync($"/SAFE/{releaseName}/file001.bin", "STOR");
        Assert.StartsWith("550 Resume rejected: no partial", stor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PassiveAndFxpPolicy_AreEnforcedOnWire()
    {
        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

        var pasv = await ftp.CommandAsync("PASV");
        Assert.StartsWith("227 ", pasv, StringComparison.Ordinal);

        var epsv = await ftp.CommandAsync("EPSV");
        Assert.StartsWith("229 ", epsv, StringComparison.Ordinal);

        var fxpPort = await ftp.CommandAsync("PORT 1,2,3,4,7,138");
        Assert.StartsWith("504 FXP not allowed", fxpPort, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PORT", "501")]
    [InlineData("PORT 1,2,3,4,7", "501")]
    [InlineData("PORT 999,0,0,1,7,138", "501")]
    [InlineData("PORT 1,2,256,1,7,138", "501")]
    [InlineData("PORT 1,2,3,1,70000,1", "501")]
    [InlineData("PORT 1,2,3,1,7,256", "501")]
    [InlineData("PORT invalid,cmd", "501")]
    [InlineData("EPRT", "501")]
    [InlineData("EPRT |1|127.0.0.1|7|138", "501")]
    [InlineData("EPRT |3|127.0.0.1|138|", "501")]
    [InlineData("EPRT |1|127.0.0.256|7|138|", "501")]
    [InlineData("EPRT |1|127.0.0.1|70000|", "501")]
    [InlineData("EPRT |2|9999::1|7010|", "504")]
    public async Task InvalidActiveCommands_DontDropControlConnection(string command, string prefix)
    {
        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

        var response = await ftp.CommandAsync(command);
        Assert.StartsWith(prefix, response, StringComparison.Ordinal);

        var noop = await ftp.CommandAsync("NOOP");
        Assert.StartsWith("200 ", noop, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestStorResume_WithMatchingPartialCrc_AppendsAtRequestedOffset()
    {
        var releaseName = $"RESUME.GOODCRC.{Guid.NewGuid():N}-ZLS";
        var physicalReleasePath = Path.Combine(_fixture.RootPath, "SAFE", releaseName);
        Directory.CreateDirectory(physicalReleasePath);

        var partialPath = Path.Combine(physicalReleasePath, "file001.bin");
        var partial = Encoding.ASCII.GetBytes("GOOD");
        var tail = Encoding.ASCII.GetBytes("TAIL");
        await File.WriteAllBytesAsync(partialPath, partial);
        await File.WriteAllTextAsync(partialPath + ".partcrc", Crc32.ToHex(Crc32.Compute(partial)));

        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
        await ftp.UploadPassiveAsync($"/SAFE/{releaseName}/file001.bin", tail, restOffset: partial.Length);

        var finalBytes = await File.ReadAllBytesAsync(partialPath);
        Assert.Equal("GOODTAIL", Encoding.ASCII.GetString(finalBytes));
    }

    [Fact]
    public async Task ConcurrentDownloadListAndDelete_ChurnsReleaseWithoutServerErrors()
    {
        var releasePath = $"/0DAY/CHURN.{Guid.NewGuid():N}-ZLS";
        var physicalReleasePath = Path.Combine(_fixture.RootPath, "0DAY", Path.GetFileName(releasePath));
        var stablePayload = Encoding.ASCII.GetBytes(new string('S', 64 * 1024));

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await admin.CreateDirectory(releasePath);
        }

        for (var i = 0; i < 18; i++)
        {
            await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
            await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
            await ftp.UploadPassiveAsync($"{releasePath}/stable-{i:D2}.bin", stablePayload);
        }

        var tasks = new List<Task>();

        for (var i = 0; i < 24; i++)
        {
            var fileIndex = i % 12;
            tasks.Add(Task.Run(async () =>
            {
                await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
                await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
                var bytes = await ftp.DownloadPassiveAsync($"{releasePath}/stable-{fileIndex:D2}.bin");
                Assert.Equal(stablePayload.Length, bytes.Length);
            }));
        }

        for (var i = 0; i < 12; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
                await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
                var cwd = await ftp.CommandAsync($"CWD {releasePath}");
                Assert.StartsWith("250 ", cwd, StringComparison.Ordinal);
                var listing = await ftp.ListPassiveAsync(".");
                Assert.True(
                    listing.Contains("stable-", StringComparison.Ordinal),
                    $"LIST . after CWD {releasePath} did not include uploaded files. Listing:\n{listing}");
            }));
        }

        for (var i = 0; i < 6; i++)
        {
            var deleteIndex = i + 12;
            tasks.Add(Task.Run(async () =>
            {
                await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
                await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
                var delete = await ftp.CommandAsync($"DELE {releasePath}/stable-{deleteIndex:D2}.bin");
                Assert.True(
                    delete.StartsWith("250 ", StringComparison.Ordinal) ||
                    delete.StartsWith("550 ", StringComparison.Ordinal),
                    $"Unexpected DELE response: {delete}");
            }));
        }

        await Task.WhenAll(tasks);

        Assert.True(Directory.Exists(physicalReleasePath), "Release directory disappeared during churn test.");
        Assert.True(Directory.EnumerateFiles(physicalReleasePath, "stable-*.bin").Any(), "All stable files were unexpectedly deleted.");
    }

    [Fact]
    public async Task ConcurrentStor_ToSameFile_ReturnsSingleFailureWithoutBogusFinal226()
    {
        var releasePath = $"/0DAY/CONFLICT.{Guid.NewGuid():N}-ZLS";

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await admin.CreateDirectory(releasePath);
        }

        await using var first = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await first.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

        await using var second = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await second.LoginAsync(_fixture.RaceAccounts[0].UserName, _fixture.RaceAccounts[0].Password);

        var remotePath = $"{releasePath}/same-file.bin";
        using var firstData = await first.StartPassiveStoreAsync(remotePath);
        var firstStream = firstData.GetStream();
        await firstStream.WriteAsync(Encoding.ASCII.GetBytes(new string('A', 8192))).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await firstStream.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var secondReply = await second.TryUploadPassiveAsync(remotePath, Encoding.ASCII.GetBytes("second-writer"));
        Assert.True(
            secondReply.StartsWith("426 ", StringComparison.Ordinal) ||
            secondReply.StartsWith("451 ", StringComparison.Ordinal),
            $"Unexpected conflicting STOR response: {secondReply}");

        var noop = await second.CommandAsync("NOOP");
        Assert.StartsWith("200 ", noop, StringComparison.Ordinal);

        firstData.Client.Shutdown(SocketShutdown.Send);
        var firstReply = await first.ReadResponseLineAsync();
        Assert.StartsWith("226 ", firstReply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveModeStor_UploadsFileAndUpdatesRaceState()
    {
        var releasePath = $"/0DAY/ACTIVE.{Guid.NewGuid():N}-ZLS";
        var payload = Encoding.ASCII.GetBytes("active-mode-upload-payload");

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await admin.CreateDirectory(releasePath);
        }

        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
        await ftp.UploadActiveAsync($"{releasePath}/active.bin", payload);

        var phys = Path.Combine(_fixture.RootPath, "0DAY", Path.GetFileName(releasePath), "active.bin");
        Assert.Equal(payload, await File.ReadAllBytesAsync(phys));

        Assert.True(_fixture.Runtime.RaceEngine.TryGetRace(releasePath, out var race));
        Assert.True(race.UserBytes.ContainsKey(_fixture.NormalUser));
    }

    [Fact]
    public async Task ActiveModeDisabledUser_IsDeniedBeforeDataEndpointIsAccepted()
    {
        var original = _fixture.Runtime.UserStore.FindUser(_fixture.NormalUser);
        Assert.NotNull(original);
        Assert.True(
            _fixture.Runtime.UserStore.TryUpdateUser(original! with { AllowActiveMode = false }, out var updateError),
            updateError);

        try
        {
            await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
            await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);

            var port = await ftp.CommandAsync("PORT 127,0,0,1,7,138");
            Assert.StartsWith("550 Permission denied.", port, StringComparison.Ordinal);

            var eprt = await ftp.CommandAsync("EPRT |1|127.0.0.1|1930|");
            Assert.StartsWith("550 Permission denied.", eprt, StringComparison.Ordinal);

            var noop = await ftp.CommandAsync("NOOP");
            Assert.StartsWith("200 ", noop, StringComparison.Ordinal);
        }
        finally
        {
            _fixture.Runtime.UserStore.TryUpdateUser(original!, out _);
        }
    }

    [Fact]
    public async Task ConcurrentGroupAddAndListing_AndConcurrentDeletes_DoNotCorruptRuntimeGroupState()
    {
        var token = Guid.NewGuid().ToString("N");
        var groups = Enumerable.Range(0, 32)
            .Select(i => $"RTEST_{token}_{i:D2}")
            .ToArray();

        async Task<string> SendAdminCommandAsync(string command)
        {
            await using var admin = await RawFtpControlSession.ConnectAsync(_fixture.Port);
            await admin.LoginAsync(_fixture.GAdminUser, _fixture.GAdminPass);
            return await admin.CommandCollectAsync(command);
        }

        async Task EnsureSuccessAsync(Task<string> op)
        {
            var response = await op;
            var codeText = response.AsSpan(0, Math.Min(3, response.Length));
            var parsed = int.TryParse(codeText, out var code);
            Assert.True(parsed, $"Invalid FTP reply: {response}");
            Assert.True(code >= 200 && code <= 299, $"Command failed: {response}");
        }

        // Concurrently add a lot of groups while also forcing SITE GROUPS enumeration from other sessions.
        var addTasks = groups.Select(g => EnsureSuccessAsync(SendAdminCommandAsync($"SITE GROUPADD {g}")));
        var listTasks = Enumerable.Range(0, 80)
            .Select(_ => EnsureSuccessAsync(SendAdminCommandAsync("SITE GROUPS")));

        await Task.WhenAll(addTasks.Concat(listTasks));

        await using var verifyClient = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await verifyClient.LoginAsync(_fixture.GAdminUser, _fixture.GAdminPass);
        var afterAdds = await verifyClient.CommandCollectAsync("SITE GROUPS");
        foreach (var groupName in groups)
            Assert.Contains(groupName, afterAdds);

        // Now run concurrent deletes on every added group.
        var deleteTasks = groups.Select(g => EnsureSuccessAsync(SendAdminCommandAsync($"SITE GROUPDEL {g}")));
        await Task.WhenAll(deleteTasks);

        await using var verifyClient2 = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await verifyClient2.LoginAsync(_fixture.GAdminUser, _fixture.GAdminPass);
        var afterDeletes = await verifyClient2.CommandCollectAsync("SITE GROUPS");
        foreach (var groupName in groups)
            Assert.DoesNotContain(groupName, afterDeletes);
    }

    [Fact]
    public async Task DeleteAndRename_DoNotLeaveStaleFileVisibility()
    {
        var releasePath = $"/0DAY/MUTATE.{Guid.NewGuid():N}-ZLS";

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await admin.CreateDirectory(releasePath);
        }

        await using var ftp = await RawFtpControlSession.ConnectAsync(_fixture.Port);
        await ftp.LoginAsync(_fixture.NormalUser, _fixture.NormalPass);
        await ftp.UploadPassiveAsync($"{releasePath}/delete-me.bin", Encoding.ASCII.GetBytes("delete-me"));

        var sizeBeforeDelete = await ftp.CommandAsync($"SIZE {releasePath}/delete-me.bin");
        Assert.StartsWith("213 ", sizeBeforeDelete, StringComparison.Ordinal);

        var delete = await ftp.CommandAsync($"DELE {releasePath}/delete-me.bin");
        Assert.StartsWith("250 ", delete, StringComparison.Ordinal);

        var sizeAfterDelete = await ftp.CommandAsync($"SIZE {releasePath}/delete-me.bin");
        Assert.StartsWith("550 ", sizeAfterDelete, StringComparison.Ordinal);

        await ftp.UploadPassiveAsync($"{releasePath}/old-name.bin", Encoding.ASCII.GetBytes("rename-me"));
        var rnfr = await ftp.CommandAsync($"RNFR {releasePath}/old-name.bin");
        Assert.StartsWith("350 ", rnfr, StringComparison.Ordinal);
        var rnto = await ftp.CommandAsync($"RNTO {releasePath}/new-name.bin");
        Assert.StartsWith("250 ", rnto, StringComparison.Ordinal);

        var oldSize = await ftp.CommandAsync($"SIZE {releasePath}/old-name.bin");
        Assert.StartsWith("550 ", oldSize, StringComparison.Ordinal);

        var newSize = await ftp.CommandAsync($"SIZE {releasePath}/new-name.bin");
        Assert.StartsWith("213 ", newSize, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiClient_ConcurrentStor_SharesReleaseRaceStatsAndConsistentZipscript()
    {
        var releaseName = $"RACE.MULTI.{Guid.NewGuid():N}-ZLS";
        var releasePath = $"/0DAY/{releaseName}";

        using (var admin = await _fixture.CreateClientAsync(_fixture.GAdminUser, _fixture.GAdminPass))
        {
            await admin.CreateDirectory(releasePath);
        }

        var uploaders = _fixture.RaceAccounts
            .Take(4)
            .Select((account, index) => (
                account.UserName,
                account.Password,
                FileName: $"file-{index:00}.bin",
                Data: Encoding.ASCII.GetBytes($"race-payload-{index:D2}-{Guid.NewGuid():N}"),
                Index: index))
            .ToArray();

        var sfvEntries = uploaders
            .Select(u => (u.FileName, Crc32.ToHex(Crc32.Compute(u.Data))))
            .ToArray();
        var sfvBytes = Encoding.UTF8.GetBytes(BuildSfv(sfvEntries));
        var expectedTotalBytes = uploaders.Sum(x => x.Data.LongLength) + sfvBytes.LongLength;

        await using (var sfvClient = await RawFtpControlSession.ConnectAsync(_fixture.Port))
        {
            await sfvClient.LoginAsync(_fixture.GAdminUser, _fixture.GAdminPass);
            var sfvReply = await sfvClient.TryUploadPassiveAsync($"{releasePath}/release.sfv", sfvBytes);
            Assert.StartsWith("226 ", sfvReply, StringComparison.Ordinal);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var zipscript = _fixture.Runtime.Zipscript;
        var monitor = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (zipscript is not null)
                    _ = zipscript.GetStatus(releasePath);

                _fixture.Runtime.RaceEngine.TryGetRace(releasePath, out _);
                await Task.Delay(25, cts.Token);
            }
        });

        var uploadTasks = uploaders.Select(async uploader =>
        {
            await using var client = await RawFtpControlSession.ConnectAsync(_fixture.Port);
            await client.LoginAsync(uploader.UserName, uploader.Password);

            var reply = await client.TryUploadPassiveAsync(
                $"{releasePath}/{uploader.FileName}",
                uploader.Data);
            Assert.StartsWith("226 ", reply, StringComparison.Ordinal);
        });

        await Task.WhenAll(uploadTasks);
        cts.Cancel();
        try
        {
            await monitor;
        }
        catch (TaskCanceledException)
        {
        }

        Assert.True(
            _fixture.Runtime.RaceEngine.TryGetRace(releasePath, out var race),
            "RaceEngine did not track concurrent release uploads.");
        Assert.Equal(uploaders.Length + 1, race.FileCount); // +1 = SFV upload
        Assert.Equal(expectedTotalBytes, race.TotalBytes);

        foreach (var uploader in uploaders)
        {
            Assert.True(race.UserBytes.ContainsKey(uploader.UserName), $"Missing uploader stats for {uploader.UserName}");
            Assert.True(race.UserBytes[uploader.UserName] > 0, $"Uploader {uploader.UserName} recorded no bytes.");
        }

        var status = _fixture.Runtime.Zipscript?.GetStatus(releasePath);
        Assert.NotNull(status);
        var releaseStatus = status!;
        Assert.True(releaseStatus.HasSfv);
        Assert.True(releaseStatus.IsComplete);
        Assert.Equal(uploaders.Length, releaseStatus.Files.Count);

        foreach (var uploader in uploaders)
        {
            var file = Assert.Single(
                releaseStatus.Files,
                f => f.FileName.Equals(uploader.FileName, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ZipscriptFileState.Ok, file.State);
            Assert.NotNull(file.ExpectedCrc);
            Assert.NotNull(file.ActualCrc);
            Assert.Equal(Crc32.Compute(uploader.Data), file.ActualCrc);
        }
    }

    private static string BuildSfv(IEnumerable<(string FileName, string ExpectedHex)> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; SFV generated by test");
        foreach (var (fileName, expectedHex) in files)
            sb.AppendLine($"{fileName} {expectedHex}");
        return sb.ToString();
    }

    private sealed class RawFtpControlSession : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private RawFtpControlSession(TcpClient client)
        {
            _client = client;
            var stream = client.GetStream();
            _reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            _writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
        }

        public static async Task<RawFtpControlSession> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(5));
            var session = new RawFtpControlSession(client);
            var banner = await session.ReadResponseAsync();
            Assert.StartsWith("220 ", banner, StringComparison.Ordinal);
            return session;
        }

        public async Task LoginAsync(string userName, string password)
        {
            var user = await CommandAsync($"USER {userName}");
            Assert.StartsWith("331 ", user, StringComparison.Ordinal);

            var pass = await CommandAsync($"PASS {password}");
            Assert.StartsWith("230 ", pass, StringComparison.Ordinal);
        }

        public async Task<string> CommandAsync(string command)
        {
            await _writer.WriteLineAsync(command).WaitAsync(TimeSpan.FromSeconds(5));
            return await ReadResponseAsync();
        }

        public async Task<string> CommandCollectAsync(string command)
        {
            await _writer.WriteLineAsync(command).WaitAsync(TimeSpan.FromSeconds(5));
            var firstLine = await ReadResponseAsync();

            if (firstLine.Length < 4 || firstLine[3] != '-')
                return firstLine;

            var code = firstLine[..3];
            var lines = new List<string> { firstLine };

            while (true)
            {
                var line = await ReadResponseAsync();
                lines.Add(line);
                if (line.StartsWith(code + " ", StringComparison.Ordinal))
                    break;
            }

            return string.Join('\n', lines);
        }

        public Task<string> CommandAsync(string argument, string command)
        {
            return CommandAsync($"{command} {argument}");
        }

        public Task<string> ReadResponseLineAsync() => ReadResponseAsync();

        public async Task UploadPassiveAsync(string remotePath, byte[] payload, int? restOffset = null)
        {
            var reply = await TryUploadPassiveAsync(remotePath, payload, restOffset);
            Assert.StartsWith("226 ", reply, StringComparison.Ordinal);
        }

        public async Task<string> TryUploadPassiveAsync(string remotePath, byte[] payload, int? restOffset = null)
        {
            var type = await CommandAsync("TYPE I");
            Assert.StartsWith("200 ", type, StringComparison.Ordinal);

            if (restOffset is > 0)
            {
                var rest = await CommandAsync($"REST {restOffset.Value}");
                Assert.StartsWith("350 ", rest, StringComparison.Ordinal);
            }

            using var dataClient = await StartPassiveStoreAsync(remotePath);
            await WriteAndShutdownAsync(dataClient, payload);

            return await ReadResponseAsync();
        }

        public async Task<TcpClient> StartPassiveStoreAsync(string remotePath)
        {
            var pasv = await CommandAsync("PASV");
            Assert.StartsWith("227 ", pasv, StringComparison.Ordinal);
            var (host, port) = ParsePassiveEndpoint(pasv);

            var dataClient = new TcpClient();
            await dataClient.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(5));

            await _writer.WriteLineAsync($"STOR {remotePath}").WaitAsync(TimeSpan.FromSeconds(5));
            var preliminary = await ReadResponseAsync();
            Assert.StartsWith("150 ", preliminary, StringComparison.Ordinal);
            return dataClient;
        }

        public async Task UploadActiveAsync(string remotePath, byte[] payload)
        {
            var type = await CommandAsync("TYPE I");
            Assert.StartsWith("200 ", type, StringComparison.Ordinal);

            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
                var acceptTask = listener.AcceptTcpClientAsync();
                var p1 = endpoint.Port / 256;
                var p2 = endpoint.Port % 256;
                var portReply = await CommandAsync($"PORT 127,0,0,1,{p1},{p2}");
                Assert.StartsWith("200 ", portReply, StringComparison.Ordinal);

                using var dataClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                await _writer.WriteLineAsync($"STOR {remotePath}").WaitAsync(TimeSpan.FromSeconds(5));
                var preliminary = await ReadResponseAsync();
                Assert.StartsWith("150 ", preliminary, StringComparison.Ordinal);

                await WriteAndShutdownAsync(dataClient, payload);

                var completion = await ReadResponseAsync();
                Assert.StartsWith("226 ", completion, StringComparison.Ordinal);
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task WriteAndShutdownAsync(TcpClient dataClient, byte[] payload)
        {
            var dataStream = dataClient.GetStream();
            await dataStream.WriteAsync(payload).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await dataStream.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            dataClient.Client.Shutdown(SocketShutdown.Send);
        }

        public async Task<byte[]> DownloadPassiveAsync(string remotePath)
        {
            var type = await CommandAsync("TYPE I");
            Assert.StartsWith("200 ", type, StringComparison.Ordinal);

            using var dataClient = await OpenPassiveDataConnectionAsync();

            await _writer.WriteLineAsync($"RETR {remotePath}").WaitAsync(TimeSpan.FromSeconds(5));
            var preliminary = await ReadResponseAsync();
            Assert.StartsWith("150 ", preliminary, StringComparison.Ordinal);

            await using var ms = new MemoryStream();
            await dataClient.GetStream().CopyToAsync(ms).WaitAsync(TimeSpan.FromSeconds(5));

            var completion = await ReadResponseAsync();
            Assert.StartsWith("226 ", completion, StringComparison.Ordinal);
            return ms.ToArray();
        }

        public async Task<string> ListPassiveAsync(string remotePath)
        {
            using var dataClient = await OpenPassiveDataConnectionAsync();

            await _writer.WriteLineAsync($"LIST {remotePath}").WaitAsync(TimeSpan.FromSeconds(5));
            var preliminary = await ReadResponseAsync();
            Assert.StartsWith("150 ", preliminary, StringComparison.Ordinal);

            await using var ms = new MemoryStream();
            await dataClient.GetStream().CopyToAsync(ms).WaitAsync(TimeSpan.FromSeconds(5));

            var completion = await ReadResponseAsync();
            Assert.StartsWith("226 ", completion, StringComparison.Ordinal);
            return Encoding.ASCII.GetString(ms.ToArray());
        }

        private async Task<TcpClient> OpenPassiveDataConnectionAsync()
        {
            var pasv = await CommandAsync("PASV");
            Assert.StartsWith("227 ", pasv, StringComparison.Ordinal);
            var (host, port) = ParsePassiveEndpoint(pasv);

            var dataClient = new TcpClient();
            await dataClient.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(5));
            return dataClient;
        }

        private static (string Host, int Port) ParsePassiveEndpoint(string response)
        {
            var open = response.IndexOf('(');
            var close = response.IndexOf(')', open + 1);
            Assert.True(open >= 0 && close > open, $"Invalid PASV response: {response}");

            var parts = response[(open + 1)..close].Split(',');
            Assert.Equal(6, parts.Length);

            var host = string.Join('.', parts[0], parts[1], parts[2], parts[3]);
            var port = (int.Parse(parts[4]) << 8) + int.Parse(parts[5]);
            return (host, port);
        }

        private async Task<string> ReadResponseAsync()
        {
            var line = await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (line is null)
                throw new IOException("FTP server closed the control connection.");

            return line;
        }

        public ValueTask DisposeAsync()
        {
            _reader.Dispose();
            _writer.Dispose();
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
