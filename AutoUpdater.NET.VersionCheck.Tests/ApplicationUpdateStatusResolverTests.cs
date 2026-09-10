using AwesomeAssertions;
using NUnit.Framework;

namespace AutoUpdaterDotNET.VersionCheck.Tests;

[TestFixture]
public class ApplicationUpdateStatusResolverTests
{
    [Test]
    public void EqualVersions_Resolve_ReturnsUpToDate()
    {
        var status = ApplicationUpdateStatusResolver.Resolve("1.2.3.4", "1.2.3.4");

        status.Should().Be(ApplicationUpdateStatus.UpToDate);
    }

    [Test]
    public void DeployedVersionGreater_Resolve_ReturnsUpdateRequired()
    {
        var status = ApplicationUpdateStatusResolver.Resolve("1.2.3.4", "1.2.4.0");

        status.Should().Be(ApplicationUpdateStatus.UpdateRequired);
    }

    [Test]
    public void DeployedVersionLower_Resolve_ReturnsUpToDate()
    {
        var status = ApplicationUpdateStatusResolver.Resolve("1.2.4.0", "1.2.3.4");

        status.Should().Be(ApplicationUpdateStatus.UpToDate);
    }

    [Test]
    public void RunningVersionHasPrereleaseSuffix_DeployedVersionGreater_ReturnsUpdateRequired()
    {
        var status = ApplicationUpdateStatusResolver.Resolve("1.2.3-alpha.4", "1.2.4.0");

        status.Should().Be(ApplicationUpdateStatus.UpdateRequired);
    }

    [Test]
    public void BothVersionsHavePrereleaseSuffixOnSameCore_Resolve_ReturnsUpToDate()
    {
        var status = ApplicationUpdateStatusResolver.Resolve("1.2.3-alpha.4", "1.2.3-beta.1");

        status.Should().Be(ApplicationUpdateStatus.UpToDate);
    }

    [TestCase(" 2.0.0.0")]
    [TestCase("2.0.0.0 ")]
    [TestCase(" 2.0.0.0 ")]
    [TestCase("\n    2.3.1.0\n  ")]
    public void DeployedVersionHasSurroundingWhitespace_ResolveAgainstLowerRunningVersion_ReturnsUpdateRequired(string deployedVersion)
    {
        var status = ApplicationUpdateStatusResolver.Resolve("1.0.0.0", deployedVersion);

        status.Should().Be(ApplicationUpdateStatus.UpdateRequired);
    }

    [TestCase(null, "1.0.0.0")]
    [TestCase("1.0.0.0", null)]
    [TestCase("", "1.0.0.0")]
    [TestCase("1.0.0.0", "")]
    [TestCase("   ", "1.0.0.0")]
    [TestCase("notAVersion", "1.0.0.0")]
    [TestCase("1.0.0.0", "notAVersion")]
    [TestCase("1", "1.0.0.0")]
    public void NullEmptyOrUnparseableVersion_Resolve_ReturnsUnknown(string runningVersion, string deployedVersion)
    {
        var status = ApplicationUpdateStatusResolver.Resolve(runningVersion, deployedVersion);

        status.Should().Be(ApplicationUpdateStatus.Unknown);
    }
}
