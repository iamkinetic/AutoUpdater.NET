using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ZipExtractor
{
    /// <summary>
    ///     Makes sure every instance of the application being updated is gone before anything on disk is touched.
    ///     The instance that started the update is only one of them: when a second instance triggers the update, the first
    ///     one keeps every assembly mapped and nothing in AutoUpdater.NET asks it to close. Instances are matched on the
    ///     executable image path, which identifies one installation and therefore one environment.
    /// </summary>
    public class ApplicationCloser
    {
        private static readonly TimeSpan CloseMainWindowTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

        private readonly CommandLineOptions _options;
        private readonly UpdateLog _log;

        public ApplicationCloser(CommandLineOptions options, UpdateLog log)
        {
            _options = options;
            _log = log;
        }

        public bool TryClose(out string failureReason)
        {
            failureReason = null;
            var instances = CollectInstances();

            try
            {
                if (instances.Count == 0)
                {
                    _log.Write("No running instance of the application was found.");
                    return true;
                }

                _log.Write($"Found {instances.Count} running instance(s): " +
                           string.Join(", ", instances.Select(instance => instance.Id.ToString())));

                return TryCloseAll(instances, out failureReason);
            }
            finally
            {
                foreach (var instance in instances)
                {
                    instance.Dispose();
                }
            }
        }

        private bool TryCloseAll(IList<Process> instances, out string failureReason)
        {
            failureReason = null;

            _log.Write($"Waiting up to {_options.ForceCloseTimeout.TotalSeconds:0} s for the application to exit...");
            if (WaitForAllToExit(instances, _options.ForceCloseTimeout))
            {
                _log.Write("Application exited on its own.");
                return true;
            }

            if (!_options.ForceCloseApplication)
            {
                failureReason = $"{Describe(AliveInstances(instances))} still running and force close is disabled";
                return false;
            }

            _log.Write($"{Describe(AliveInstances(instances))} still running. Asking the main window to close...");
            foreach (var instance in AliveInstances(instances))
            {
                RequestMainWindowClose(instance);
            }

            if (WaitForAllToExit(instances, CloseMainWindowTimeout))
            {
                _log.Write("Application closed after the close request.");
                return true;
            }

            foreach (var instance in AliveInstances(instances))
            {
                _log.Write($"Killing process {instance.Id}...");
                TryKill(instance);
            }

            if (WaitForAllToExit(instances, KillTimeout))
            {
                _log.Write("All instances of the application were killed.");
                return true;
            }

            failureReason = $"{Describe(AliveInstances(instances))} could not be terminated";
            return false;
        }

        /// <summary>
        ///     The process id handed over by AutoUpdater.NET plus every other process running the same executable. Both are
        ///     needed: the process id is authoritative for the instance that started the update, and the image path finds the
        ///     instances that were never told to close.
        /// </summary>
        private IList<Process> CollectInstances()
        {
            var instances = new List<Process>();
            var seenIds = new HashSet<int>();

            var startingInstance = FindStartingInstance();
            if (startingInstance != null)
            {
                instances.Add(startingInstance);
                seenIds.Add(startingInstance.Id);
            }

            foreach (var candidate in GetProcessesByExecutableName())
            {
                if (seenIds.Contains(candidate.Id))
                {
                    candidate.Dispose();
                    continue;
                }

                var imagePath = ProcessImage.TryGetPath(candidate);
                if (imagePath == null)
                {
                    _log.Write($"Cannot read the executable path of process {candidate.Id}, so it is left alone.");
                    candidate.Dispose();
                    continue;
                }

                if (!ProcessImage.IsSameFile(imagePath, _options.ExecutablePath))
                {
                    _log.Write($"Process {candidate.Id} runs '{imagePath}', which is another installation.");
                    candidate.Dispose();
                    continue;
                }

                instances.Add(candidate);
                seenIds.Add(candidate.Id);
            }

            return instances;
        }

        private Process FindStartingInstance()
        {
            if (!_options.ApplicationProcessId.HasValue)
            {
                _log.Write("No process id was provided. Instances will only be matched by executable path.");
                return null;
            }

            var processId = _options.ApplicationProcessId.Value;
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                _log.Write($"Application process {processId} has already exited.");
                return null;
            }

            // A process id is reused once its process is gone, so confirm this is still the application.
            var imagePath = ProcessImage.TryGetPath(process);
            if (imagePath != null && !ProcessImage.IsSameFile(imagePath, _options.ExecutablePath))
            {
                _log.Write($"Process {processId} now runs '{imagePath}'. The application has exited and its process " +
                           "id was reused.");
                process.Dispose();
                return null;
            }

            return process;
        }

        private IEnumerable<Process> GetProcessesByExecutableName()
        {
            var executableName = Path.GetFileNameWithoutExtension(_options.ExecutablePath);
            if (string.IsNullOrEmpty(executableName))
            {
                return Enumerable.Empty<Process>();
            }

            try
            {
                return Process.GetProcessesByName(executableName);
            }
            catch (Exception exception)
            {
                _log.WriteException($"Failed to list processes named '{executableName}'", exception);
                return Enumerable.Empty<Process>();
            }
        }

        private bool WaitForAllToExit(IEnumerable<Process> instances, TimeSpan timeout)
        {
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                if (!AliveInstances(instances).Any())
                {
                    return true;
                }

                if (deadline.Elapsed >= timeout)
                {
                    return false;
                }

                Thread.Sleep(PollInterval);
            }
        }

        private static IEnumerable<Process> AliveInstances(IEnumerable<Process> instances)
        {
            return instances.Where(instance => !HasExited(instance)).ToList();
        }

        private static bool HasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (Exception)
            {
                // A process we can no longer query cannot be waited on or killed either.
                return true;
            }
        }

        private void RequestMainWindowClose(Process process)
        {
            try
            {
                process.CloseMainWindow();
            }
            catch (Exception exception)
            {
                _log.WriteException($"Failed to ask process {process.Id} to close", exception);
            }
        }

        private void TryKill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception exception)
            {
                _log.WriteException($"Failed to kill process {process.Id}", exception);
            }
        }

        private static string Describe(IEnumerable<Process> instances)
        {
            var ids = instances.Select(instance => instance.Id.ToString()).ToList();
            return ids.Count == 1
                ? $"process {ids[0]} is"
                : $"processes {string.Join(", ", ids)} are";
        }
    }
}
