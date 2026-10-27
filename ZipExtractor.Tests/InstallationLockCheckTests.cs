using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;

namespace ZipExtractor.Tests;

[TestFixture]
public class InstallationLockCheckTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShortPollInterval = TimeSpan.FromMilliseconds(20);

    private string _installationPath;
    private string _executablePath;
    private string _logFilePath;
    private UpdateLog _log;

    [SetUp]
    public void CreateInstallationFolder()
    {
        _installationPath = Path.Combine(Path.GetTempPath(), "InstallationLockCheckTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installationPath);
        _executablePath = Path.Combine(_installationPath, "Application.exe");
        File.WriteAllText(_executablePath, "executable");
        File.WriteAllText(Path.Combine(_installationPath, "Library.dll"), "library");
        _logFilePath = Path.Combine(_installationPath, "update.log");
        _log = new UpdateLog(_logFilePath);
    }

    [TearDown]
    public void DeleteInstallationFolder()
    {
        Directory.Delete(_installationPath, true);
    }

    [Test]
    public void NoLockedFile_TryVerifyWritable_ReturnsTrueWithoutWaiting()
    {
        var check = CreateCheck(ShortTimeout);
        var startedAt = DateTime.UtcNow;

        var isWritable = check.TryVerifyWritable(out var failureReason);

        isWritable.Should().BeTrue();
        failureReason.Should().BeNull();
        (DateTime.UtcNow - startedAt).Should().BeLessThan(ShortTimeout);
    }

    [Test]
    public async Task FileReleasedBeforeTimeout_TryVerifyWritable_ReturnsTrue()
    {
        using var holder = new FileStream(_executablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(() =>
        {
            Thread.Sleep(300);
            holder.Dispose();
        });
        var check = CreateCheck(TimeSpan.FromSeconds(10));

        var isWritable = check.TryVerifyWritable(out var failureReason);

        await release;
        isWritable.Should().BeTrue();
        failureReason.Should().BeNull();
        var logContent = File.ReadAllText(_logFilePath);
        logContent.Should().Contain("Waiting up to");
        logContent.Should().Contain("Locked files were released after");
    }

    [Test]
    public void ProbedFileReleasedThenDeletedDuringWait_TryVerifyWritable_ReturnsTrueWithoutWaitingForTheTimeout()
    {
        var holder = new FileStream(_executablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var timeout = TimeSpan.FromSeconds(10);
        var check = CreateCheck(timeout);
        var stopwatch = Stopwatch.StartNew();
        var deletion = Task.Run(() =>
        {
            Thread.Sleep(300);
            holder.Dispose();
            File.Delete(_executablePath);
        });

        var isWritable = check.TryVerifyWritable(out var failureReason);

        deletion.Wait();
        isWritable.Should().BeTrue();
        failureReason.Should().BeNull();
        stopwatch.Elapsed.Should().BeLessThan(timeout);
    }

    [Test]
    public void FileHeldLongerThanTimeout_TryVerifyWritable_ReturnsFalseNamingTheFile()
    {
        using var holder = new FileStream(_executablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var timeout = TimeSpan.FromMilliseconds(300);
        var check = CreateCheck(timeout);
        var stopwatch = Stopwatch.StartNew();

        var isWritable = check.TryVerifyWritable(out var failureReason);

        isWritable.Should().BeFalse();
        failureReason.Should().StartWith("'Application.exe' is still in use");
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(timeout);
        var logContent = File.ReadAllText(_logFilePath);
        logContent.Should().Contain("Waiting up to");
        logContent.Should().Contain("Files are still locked after");
    }

    private InstallationLockCheck CreateCheck(TimeSpan waitTimeout)
    {
        return new InstallationLockCheck(_installationPath + Path.DirectorySeparatorChar, _executablePath, _log,
            waitTimeout, ShortPollInterval);
    }
}
