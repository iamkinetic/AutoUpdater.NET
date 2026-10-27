using System;
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
    private UpdateLog _log;

    [SetUp]
    public void CreateInstallationFolder()
    {
        _installationPath = Path.Combine(Path.GetTempPath(), "InstallationLockCheckTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installationPath);
        _executablePath = Path.Combine(_installationPath, "Application.exe");
        File.WriteAllText(_executablePath, "executable");
        File.WriteAllText(Path.Combine(_installationPath, "Library.dll"), "library");
        _log = new UpdateLog(Path.Combine(_installationPath, "update.log"));
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
    }

    [Test]
    public void FileHeldLongerThanTimeout_TryVerifyWritable_ReturnsFalseNamingTheFile()
    {
        using var holder = new FileStream(_executablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var check = CreateCheck(TimeSpan.FromMilliseconds(300));

        var isWritable = check.TryVerifyWritable(out var failureReason);

        isWritable.Should().BeFalse();
        failureReason.Should().StartWith("'Application.exe' is still in use");
    }

    private InstallationLockCheck CreateCheck(TimeSpan waitTimeout)
    {
        return new InstallationLockCheck(_installationPath + Path.DirectorySeparatorChar, _executablePath, _log,
            waitTimeout, ShortPollInterval);
    }
}
