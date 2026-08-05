using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ZipExtractor
{
    /// <summary>
    ///     Makes sure the application being updated is gone before anything on disk is touched.
    ///     Identification is by process id rather than by module path: a 32 bit ZipExtractor cannot read MainModule of a
    ///     64 bit application, so path matching silently matched nothing and the update started while the application was
    ///     still running. A process id also scopes the wait to one installation, which is what a per-environment install
    ///     needs.
    /// </summary>
    public class ApplicationCloser
    {
        private static readonly TimeSpan CloseMainWindowTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(10);

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

            if (!_options.ApplicationProcessId.HasValue)
            {
                _log.Write("No process id was provided. Falling back to matching by executable path, which cannot see " +
                           "processes of a different bitness.");
                WaitByExecutablePath();
                return true;
            }

            var process = FindApplicationProcess(_options.ApplicationProcessId.Value);
            if (process == null)
            {
                return true;
            }

            using (process)
            {
                return TryClose(process, out failureReason);
            }
        }

        private bool TryClose(Process process, out string failureReason)
        {
            failureReason = null;

            _log.Write($"Waiting up to {_options.ForceCloseTimeout.TotalSeconds:0} s for process {process.Id} to exit...");
            if (WaitForExit(process, _options.ForceCloseTimeout))
            {
                _log.Write("Application exited on its own.");
                return true;
            }

            if (!_options.ForceCloseApplication)
            {
                failureReason =
                    $"process {process.Id} is still running and force close is disabled";
                return false;
            }

            _log.Write("Application did not exit. Asking its main window to close...");
            if (RequestMainWindowClose(process) && WaitForExit(process, CloseMainWindowTimeout))
            {
                _log.Write("Application closed after the close request.");
                return true;
            }

            _log.Write("Application still running. Killing it...");
            if (TryKill(process) && WaitForExit(process, KillTimeout))
            {
                _log.Write($"Application process {process.Id} was killed.");
                return true;
            }

            failureReason = $"process {process.Id} could not be terminated";
            return false;
        }

        /// <summary>
        ///     Guards against a recycled process id by comparing the process name, which stays readable across bitness
        ///     boundaries unlike MainModule.
        /// </summary>
        private Process FindApplicationProcess(int processId)
        {
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

            var expectedName = Path.GetFileNameWithoutExtension(_options.ExecutablePath);
            if (!string.IsNullOrEmpty(expectedName) &&
                !process.ProcessName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                _log.Write($"Process {processId} is now '{process.ProcessName}' instead of '{expectedName}'. " +
                           "The application has exited and its process id was reused.");
                process.Dispose();
                return null;
            }

            return process;
        }

        private bool WaitForExit(Process process, TimeSpan timeout)
        {
            try
            {
                process.WaitForExit((int) timeout.TotalMilliseconds);
                return process.HasExited;
            }
            catch (Exception exception)
            {
                _log.WriteException("Failed to wait for the application to exit", exception);
                return false;
            }
        }

        private bool RequestMainWindowClose(Process process)
        {
            try
            {
                return process.CloseMainWindow();
            }
            catch (Exception exception)
            {
                _log.WriteException("Failed to request the main window to close", exception);
                return false;
            }
        }

        private bool TryKill(Process process)
        {
            try
            {
                process.Kill();
                return true;
            }
            catch (Exception exception)
            {
                _log.WriteException("Failed to kill the application", exception);
                return false;
            }
        }

        /// <summary>
        ///     Degraded path used only when no process id is available. Kept because there is nothing better to fall back on,
        ///     but every failure is logged instead of being swallowed the way the previous implementation did.
        /// </summary>
        private void WaitByExecutablePath()
        {
            foreach (var process in Process.GetProcessesByName(
                         Path.GetFileNameWithoutExtension(_options.ExecutablePath)))
            {
                using (process)
                {
                    try
                    {
                        var modulePath = process.MainModule?.FileName;
                        if (modulePath != null &&
                            modulePath.Equals(_options.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            _log.Write($"Waiting for process {process.Id} to exit...");
                            process.WaitForExit();
                        }
                    }
                    catch (Win32Exception exception)
                    {
                        _log.WriteException(
                            $"Cannot read modules of process {process.Id}, so it cannot be waited on", exception);
                    }
                    catch (Exception exception)
                    {
                        _log.WriteException($"Failed to inspect process {process.Id}", exception);
                    }
                }
            }
        }
    }
}
