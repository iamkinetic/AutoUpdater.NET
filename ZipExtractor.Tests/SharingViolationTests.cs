using System.IO;
using AwesomeAssertions;
using NUnit.Framework;

namespace ZipExtractor.Tests;

[TestFixture]
public class SharingViolationTests
{
    [TestCase(0x80070020)]
    [TestCase(0x80070021)]
    public void SharingOrLockViolationHResult_Matches_ReturnsTrue(uint hResult)
    {
        var exception = new IOException("", unchecked((int)hResult));

        SharingViolation.Matches(exception).Should().BeTrue();
    }

    [Test]
    public void FileNotFound_Matches_ReturnsFalse()
    {
        SharingViolation.Matches(new FileNotFoundException()).Should().BeFalse();
    }

    [Test]
    public void IOExceptionWithAnotherHResult_Matches_ReturnsFalse()
    {
        var exception = new IOException("", unchecked((int)0x80070005));

        SharingViolation.Matches(exception).Should().BeFalse();
    }
}
