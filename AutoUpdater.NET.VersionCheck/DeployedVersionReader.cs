using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace AutoUpdaterDotNET.VersionCheck
{
    public class DeployedVersionReader : IDeployedVersionReader
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private readonly Uri manifestUri;
        private readonly AuthenticationHeaderValue basicAuthenticationHeader;
        private readonly HttpMessageHandler messageHandler;

        public DeployedVersionReader(string manifestUrl, string userName, string password, HttpMessageHandler messageHandler = null)
        {
            manifestUri = BuildManifestUri(manifestUrl);
            basicAuthenticationHeader = BuildBasicAuthenticationHeader(userName, password);
            this.messageHandler = messageHandler;
        }

        public async Task<string> GetDeployedVersionAsync(CancellationToken cancellationToken = default)
        {
            if (manifestUri == null)
                return null;

            try
            {
                using var httpClient = CreateHttpClient();
                using var response = await httpClient.GetAsync(manifestUri, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                var manifestContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return ExtractVersion(manifestContent);
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (XmlException)
            {
                return null;
            }
        }

        private HttpClient CreateHttpClient()
        {
            var httpClient = messageHandler == null ? new HttpClient() : new HttpClient(messageHandler, disposeHandler: false);
            httpClient.DefaultRequestHeaders.Authorization = basicAuthenticationHeader;
            httpClient.Timeout = RequestTimeout;
            return httpClient;
        }

        private static Uri BuildManifestUri(string manifestUrl)
        {
            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri))
                return null;

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri : null;
        }

        private static string ExtractVersion(string manifestContent)
        {
            var manifest = XDocument.Parse(manifestContent);
            var version = manifest.Root?.Element("version")?.Value?.Trim();
            return string.IsNullOrEmpty(version) ? null : version;
        }

        private static AuthenticationHeaderValue BuildBasicAuthenticationHeader(string userName, string password)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}"));
            return new AuthenticationHeaderValue("Basic", credentials);
        }
    }
}
