using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ZipExtractor
{
    /// <summary>
    ///     Reads the executable path of a process. Process.MainModule cannot do this across a bitness boundary, which is
    ///     what made the previous implementation blind to the very application it was updating. QueryFullProcessImageName
    ///     has no such limitation.
    /// </summary>
    public static class ProcessImage
    {
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const int MaximumPathLength = 32768;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder imageName,
            ref int size);

        public static string TryGetPath(Process process)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var imageName = new StringBuilder(MaximumPathLength);
                var size = imageName.Capacity;
                return QueryFullProcessImageName(handle, 0, imageName, ref size)
                    ? imageName.ToString()
                    : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static bool IsSameFile(string first, string second)
        {
            return !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) &&
                   string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }
}
