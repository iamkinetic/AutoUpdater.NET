using System.IO;
using System.Runtime.InteropServices;

namespace ZipExtractor
{
    public static class SharingViolation
    {
        private const int ErrorSharingViolation = 0x20;
        private const int ErrorLockViolation = 0x21;

        public static bool Matches(IOException exception)
        {
            var errorCode = Marshal.GetHRForException(exception) & 0x0000FFFF;
            return errorCode == ErrorSharingViolation || errorCode == ErrorLockViolation;
        }
    }
}
