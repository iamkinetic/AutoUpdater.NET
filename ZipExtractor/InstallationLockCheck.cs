using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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

        private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

        private readonly string _extractionPath;
        private readonly string _executablePath;
        private readonly UpdateLog _log;
        private readonly TimeSpan _waitTimeout;
        private readonly TimeSpan _pollInterval;

        public InstallationLockCheck(string extractionPath, string executablePath, UpdateLog log,
            TimeSpan? waitTimeout = null, TimeSpan? pollInterval = null)
        {
            _extractionPath = extractionPath;
            _executablePath = executablePath;
            _log = log;
            _waitTimeout = waitTimeout ?? DefaultWaitTimeout;
            _pollInterval = pollInterval ?? DefaultPollInterval;
        }

        public bool TryVerifyWritable(out string failureReason)
        {
            failureReason = null;

            var lockedFiles = GetProbedFiles().Where(IsLocked).ToList();
            if (lockedFiles.Count > 0 && !TryWaitForRelease(lockedFiles))
            {
                var filePath = lockedFiles[0];
                failureReason = $"'{Path.GetFileName(filePath)}' is still in use{DescribeHolders(filePath)}";
                return false;
            }

            _log.Write("Installation directory is writable.");
            return true;
        }

        /// <summary>
        ///     A Windows service (indexing, antivirus, shell extensions) can hold the just-exited executable for a moment, and
        ///     aborting on that blip relaunches the old application, which restarts the whole update.
        /// </summary>
        private bool TryWaitForRelease(List<string> lockedFiles)
        {
            _log.Write($"'{Path.GetFileName(lockedFiles[0])}' is in use{DescribeHolders(lockedFiles[0])}. " +
                       $"Waiting up to {_waitTimeout.TotalSeconds:0.#} s for it to be released...");

            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                lockedFiles.RemoveAll(filePath => !IsLocked(filePath));
                if (lockedFiles.Count == 0)
                {
                    _log.Write($"Locked files were released after {stopwatch.Elapsed.TotalSeconds:0.0} s.");
                    return true;
                }

                if (stopwatch.Elapsed >= _waitTimeout)
                {
                    _log.Write($"Files are still locked after {stopwatch.Elapsed.TotalSeconds:0.0} s.");
                    return false;
                }

                Thread.Sleep(_pollInterval);
            }
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
