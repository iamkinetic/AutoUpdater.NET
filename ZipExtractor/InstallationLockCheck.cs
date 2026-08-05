using System;
using System.IO;
using System.Linq;

namespace ZipExtractor
{
    /// <summary>
    ///     Proves that the installation directory can be written before the clear phase deletes anything. Aborting here
    ///     leaves a working installation behind, whereas discovering a lock halfway through the clear phase leaves an
    ///     application that cannot start and therefore cannot retry its own update.
    /// </summary>
    public class InstallationLockCheck
    {
        private const int MaximumProbedLibraries = 10;

        private readonly string _extractionPath;
        private readonly string _executablePath;
        private readonly UpdateLog _log;

        public InstallationLockCheck(string extractionPath, string executablePath, UpdateLog log)
        {
            _extractionPath = extractionPath;
            _executablePath = executablePath;
            _log = log;
        }

        public bool TryVerifyWritable(out string failureReason)
        {
            failureReason = null;

            foreach (var filePath in GetProbedFiles())
            {
                if (IsLocked(filePath))
                {
                    failureReason = $"'{Path.GetFileName(filePath)}' is still in use{DescribeHolders(filePath)}";
                    return false;
                }
            }

            _log.Write("Installation directory is writable.");
            return true;
        }

        /// <summary>
        ///     The executable plus a sample of the assemblies next to it. A loaded module cannot be opened without sharing,
        ///     so a handful of probes is enough to detect a process that is still alive without paying for all of them.
        /// </summary>
        private string[] GetProbedFiles()
        {
            var probedFiles = Enumerable.Empty<string>();

            if (File.Exists(_executablePath))
            {
                probedFiles = probedFiles.Concat(new[] {_executablePath});
            }

            try
            {
                probedFiles = probedFiles.Concat(Directory
                    .GetFiles(_extractionPath, "*.dll", SearchOption.TopDirectoryOnly)
                    .Take(MaximumProbedLibraries));
            }
            catch (Exception exception)
            {
                _log.WriteException("Failed to list assemblies to probe for locks", exception);
            }

            return probedFiles.ToArray();
        }

        private bool IsLocked(string filePath)
        {
            try
            {
                using (File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException exception)
            {
                // A read only file or a restrictive ACL also lands here. That is not a lock, and the clear phase already
                // tolerates a file it cannot delete.
                _log.WriteException($"Cannot open '{Path.GetFileName(filePath)}' for writing", exception);
                return false;
            }
        }

        private string DescribeHolders(string filePath)
        {
            try
            {
                var holders = FileUtil.WhoIsLocking(filePath);
                if (holders.Count == 0)
                {
                    return string.Empty;
                }

                var descriptions = holders.Select(holder =>
                {
                    using (holder)
                    {
                        return $"{holder.ProcessName} ({holder.Id})";
                    }
                });

                return $" by {string.Join(", ", descriptions)}";
            }
            catch (Exception exception)
            {
                _log.WriteException("Failed to determine which process holds the file", exception);
                return string.Empty;
            }
        }
    }
}
