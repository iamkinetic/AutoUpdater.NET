using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;

namespace AutoUpdaterDotNET.VersionCheck.Tests;

[TestFixture]
public class DeployedVersionReaderTests
{
    private const string AManifestUrl = "https://logicielcauca.cauca.ca/updates_survirao3/test_updates.xml";
    private const string AnHttpManifestUrl = "http://logicielcauca.cauca.ca/updates_survirao3/test_updates.xml";

    [Test]
    public async Task WellFormedManifest_GetDeployedVersionAsync_ReturnsVersion()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<item><version>2.3.1.0</version><url>https://example.org/update.zip</url></item>")
        });
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().Be("2.3.1.0");
    }

    [Test]
    public async Task PrettyPrintedManifest_GetDeployedVersionAsync_ReturnsTrimmedVersion()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<item>\n  <version>\n    2.3.1.0\n  </version>\n</item>")
        });
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().Be("2.3.1.0");
    }

    [Test]
    public async Task MalformedXml_GetDeployedVersionAsync_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<item><version>2.3.1.0</version>")
        });
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().BeNull();
    }

    [Test]
    public async Task ManifestWithNoVersionElement_GetDeployedVersionAsync_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<item><url>https://example.org/update.zip</url></item>")
        });
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().BeNull();
    }

    [Test]
    public async Task ManifestWithEmptyVersionElement_GetDeployedVersionAsync_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<item><version>   </version></item>")
        });
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().BeNull();
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task NonSuccessHttpStatus_GetDeployedVersionAsync_ReturnsNull(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().BeNull();
    }

    [Test]
    public async Task HandlerThrowsHttpRequestException_GetDeployedVersionAsync_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("network unreachable"));
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().BeNull();
    }

    [Test]
    public async Task HandlerThrowsTaskCanceledException_GetDeployedVersionAsync_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("timed out"));
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().BeNull();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("logicielcauca.ca/updates.xml")]
    [TestCase("logicielcauca.cauca.ca/updates.xml")]
    [TestCase("htp:/broken")]
    [TestCase("ftp://logicielcauca.cauca.ca/updates.xml")]
    [TestCase("file:///c:/updates.xml")]
    public async Task InvalidManifestUrl_GetDeployedVersionAsync_ReturnsNullWithoutSendingRequest(string manifestUrl)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<item><version>1.0.0.0</version></item>")
        });
        var reader = new DeployedVersionReader(manifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().BeNull();
        handler.RequestCount.Should().Be(0);
    }

    [Test]
    public async Task HttpManifestUrl_GetDeployedVersionAsync_SendsRequestAndReturnsVersion()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<item><version>2.3.1.0</version></item>")
        });
        var reader = new DeployedVersionReader(AnHttpManifestUrl, "user", "password", handler);

        var deployedVersion = await reader.GetDeployedVersionAsync();

        deployedVersion.Should().Be("2.3.1.0");
        handler.RequestCount.Should().Be(1);
    }

    [Test]
    public async Task ConfiguredCredentials_GetDeployedVersionAsync_SendsBasicAuthenticationHeader()
    {
        HttpRequestMessage capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<item><version>1.0.0.0</version></item>")
            };
        });
        var reader = new DeployedVersionReader(AManifestUrl, "user", "password", handler);

        await reader.GetDeployedVersionAsync();

        capturedRequest.Should().NotBeNull();
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization.Scheme.Should().Be("Basic");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responder(request));
        }
    }
}
