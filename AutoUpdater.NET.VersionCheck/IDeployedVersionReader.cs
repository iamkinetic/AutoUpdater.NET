using System.Threading;
using System.Threading.Tasks;

namespace AutoUpdaterDotNET.VersionCheck
{
    public interface IDeployedVersionReader
    {
        Task<string> GetDeployedVersionAsync(CancellationToken cancellationToken = default);
    }
}
