using System;
using System.Text.RegularExpressions;

namespace AutoUpdaterDotNET.VersionCheck
{
    public static partial class ApplicationUpdateStatusResolver
    {
        [GeneratedRegex(@"^\d+(\.\d+){1,3}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex VersionCorePattern();

        public static ApplicationUpdateStatus Resolve(string runningVersion, string deployedVersion)
        {
            if (!TryParseVersionCore(runningVersion, out var runningVersionCore) || !TryParseVersionCore(deployedVersion, out var deployedVersionCore))
                return ApplicationUpdateStatus.Unknown;

            return deployedVersionCore > runningVersionCore ? ApplicationUpdateStatus.UpdateRequired : ApplicationUpdateStatus.UpToDate;
        }

        private static bool TryParseVersionCore(string version, out Version versionCore)
        {
            versionCore = null;
            if (string.IsNullOrWhiteSpace(version))
                return false;

            var match = VersionCorePattern().Match(version.Trim());
            return match.Success && Version.TryParse(match.Value, out versionCore);
        }
    }
}
