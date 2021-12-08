using Semver;

namespace AutoUpdaterDotNET
{
    public static class SemVersionExtensions
    {
        public static bool IsNewerThan(this SemVersion version, SemVersion otherVersion)
        {
            return version.CompareTo(otherVersion) == 1;
        }

        public static bool IsNewerOrEqualTo(this SemVersion version, SemVersion otherVersion)
        {
            return version.CompareTo(otherVersion) >= 0;
        }

        public static bool IsOlderThan(this SemVersion version, SemVersion otherVersion)
        {
            return version.CompareTo(otherVersion) == -1;
        }

        public static bool IsOlderOrEqualTo(this SemVersion version, SemVersion otherVersion)
        {
            return version.CompareTo(otherVersion) <= 0;
        }

        public static bool IsSame(this SemVersion version, SemVersion otherVersion)
        {
            return version.CompareTo(otherVersion) == 0;
        }
    }
}