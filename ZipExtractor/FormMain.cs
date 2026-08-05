using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ZipExtractor.Properties;

namespace ZipExtractor
{
    public partial class FormMain : Form
    {
        private const int MaxRetries = 2;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

        private readonly CommandLineOptions _options;
        private readonly UpdateLog _log;
        private readonly bool _exitOnComplete;
        private BackgroundWorker _backgroundWorker;

        public FormMain(CommandLineOptions options, bool exitOnComplete = true)
        {
            InitializeComponent();
            _options = options;
            _exitOnComplete = exitOnComplete;
            _log = new UpdateLog(options.LogFilePath);
        }

        public static FormMain FromCommandLineArgs()
        {
            return new FormMain(CommandLineOptions.Parse(Environment.GetCommandLineArgs()));
        }

        private void FormMain_Shown(object sender, EventArgs e)
        {
            if (!_options.IsValid)
            {
                _log.Write("ABORTED: missing required arguments.");
                MessageBox.Show("Missing required arguments.", "ZipExtractor", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            _backgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            _backgroundWorker.DoWork += OnDoWork;
            _backgroundWorker.ProgressChanged += OnProgressChanged;
            _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;
            _backgroundWorker.RunWorkerAsync();
        }

        private void OnDoWork(object sender, DoWorkEventArgs eventArgs)
        {
            var extractionPath = EnsureTrailingSeparator(_options.ExtractionPath);

            Report("Waiting for application to exit...");
            if (!new ApplicationCloser(_options, _log).TryClose(out var stillRunningReason))
            {
                eventArgs.Result = UpdateOutcome.Aborted(stillRunningReason, false);
                return;
            }

            if (!new InstallationLockCheck(extractionPath, _options.ExecutablePath, _log)
                    .TryVerifyWritable(out var lockedReason))
            {
                eventArgs.Result = UpdateOutcome.Aborted(lockedReason, true);
                return;
            }

            if (_options.ClearAppDirectory)
            {
                Report("Clearing application directory...");
                new DirectoryCleaner(BuildPreservedEntries(), _log).Clear(extractionPath);
            }

            Extract(extractionPath, eventArgs);

            if (!eventArgs.Cancel)
            {
                eventArgs.Result = UpdateOutcome.Success();
            }
        }

        /// <summary>
        ///     The log file is preserved when it lives inside the installation directory, otherwise the clear phase would
        ///     delete the record of what it just did.
        /// </summary>
        private PreservedEntries BuildPreservedEntries()
        {
            var preserved = new PreservedEntries(_options.IgnoredNames, _options.ProtectedPatterns);

            var logDirectory = Path.GetDirectoryName(_log.FilePath);
            if (!string.IsNullOrEmpty(logDirectory) && IsSameDirectory(logDirectory, _options.ExtractionPath))
            {
                preserved.Preserve(Path.GetFileName(_log.FilePath));
            }

            return preserved;
        }

        private void Extract(string extractionPath, DoWorkEventArgs eventArgs)
        {
            using (var archive = ZipFile.OpenRead(_options.ZipFilePath))
            {
                var entries = archive.Entries;
                _log.Write($"Found total of {entries.Count} files and folders inside the zip file.");

                var progress = 0;
                for (var index = 0; index < entries.Count; index++)
                {
                    if (_backgroundWorker.CancellationPending)
                    {
                        eventArgs.Cancel = true;
                        _log.Write("ABORTED: extraction was cancelled.");
                        return;
                    }

                    var entry = entries[index];
                    var currentFile = string.Format(Resources.CurrentFileExtracting, entry.FullName);
                    _backgroundWorker.ReportProgress(progress, currentFile);

                    ExtractEntry(entry, extractionPath);

                    progress = (index + 1) * 100 / entries.Count;
                    _backgroundWorker.ReportProgress(progress, currentFile);
                    _log.Write($"{currentFile} [{progress}%]");
                }
            }
        }

        /// <summary>
        ///     Retries a locked entry a few times and then gives up. It used to prompt the user to retry, which turned a
        ///     logged failure into an indefinite hang on an unattended machine.
        /// </summary>
        private void ExtractEntry(ZipArchiveEntry entry, string extractionPath)
        {
            var filePath = Path.Combine(extractionPath, entry.FullName);
            var retries = 0;

            while (true)
            {
                try
                {
                    if (entry.IsDirectory())
                    {
                        return;
                    }

                    var parentDirectory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(parentDirectory) && !Directory.Exists(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    entry.ExtractToFile(filePath, true);
                    return;
                }
                catch (IOException exception) when (IsSharingViolation(exception) && retries < MaxRetries)
                {
                    retries++;
                    _log.Write($"'{filePath}' is in use{DescribeHolders(filePath)}. " +
                               $"Retry {retries} of {MaxRetries} in {RetryDelay.TotalSeconds:0} s.");
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        private static bool IsSharingViolation(IOException exception)
        {
            const int errorSharingViolation = 0x20;
            const int errorLockViolation = 0x21;
            var errorCode = Marshal.GetHRForException(exception) & 0x0000FFFF;
            return errorCode == errorSharingViolation || errorCode == errorLockViolation;
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

                var names = new string[holders.Count];
                for (var index = 0; index < holders.Count; index++)
                {
                    using (var holder = holders[index])
                    {
                        names[index] = $"{holder.ProcessName} ({holder.Id})";
                    }
                }

                return $" by {string.Join(", ", names)}";
            }
            catch (Exception exception)
            {
                _log.WriteException("Failed to determine which process holds the file", exception);
                return string.Empty;
            }
        }

        private void OnProgressChanged(object sender, ProgressChangedEventArgs eventArgs)
        {
            progressBar.Value = eventArgs.ProgressPercentage;
            textBoxInformation.Text = eventArgs.UserState?.ToString();
            if (textBoxInformation.Text != null)
            {
                textBoxInformation.SelectionStart = textBoxInformation.Text.Length;
                textBoxInformation.SelectionLength = 0;
            }
        }

        private void OnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs eventArgs)
        {
            try
            {
                if (eventArgs.Error != null)
                {
                    throw eventArgs.Error;
                }

                if (eventArgs.Cancelled)
                {
                    return;
                }

                var outcome = (UpdateOutcome) eventArgs.Result;
                if (outcome.Succeeded)
                {
                    textBoxInformation.Text = @"Finished";
                    _log.Write("COMPLETED: update extracted successfully.");
                    StartUpdatedApplication();
                    return;
                }

                textBoxInformation.Text = @"Update cancelled";
                _log.Write($"ABORTED: {outcome.AbortReason}. The installation directory was left untouched.");
                if (outcome.ShouldRelaunchApplication)
                {
                    StartUpdatedApplication();
                }
            }
            catch (Exception exception)
            {
                _log.Write($"FAILED: {exception}");
                MessageBox.Show(exception.Message, exception.GetType().ToString(),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_exitOnComplete)
                {
                    Application.Exit();
                }
                else
                {
                    Close();
                }
            }
        }

        private void StartUpdatedApplication()
        {
            try
            {
                var processStartInfo = new ProcessStartInfo(_options.ExecutablePath);
                if (!string.IsNullOrEmpty(_options.ExecutableArguments))
                {
                    processStartInfo.Arguments = _options.ExecutableArguments;
                }

                Process.Start(processStartInfo);
                _log.Write("Successfully launched the application.");
            }
            catch (Win32Exception exception)
            {
                if (exception.NativeErrorCode != 1223)
                {
                    throw;
                }

                _log.WriteException("The user cancelled the application launch", exception);
            }
        }

        private void Report(string message)
        {
            _log.Write(message);
            _backgroundWorker.ReportProgress(0, message);
        }

        /// <summary>
        ///     Without this a malicious zip file could try to traverse outside of the expected extraction path.
        /// </summary>
        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static bool IsSameDirectory(string first, string second)
        {
            return string.Equals(
                Path.GetFullPath(EnsureTrailingSeparator(first)),
                Path.GetFullPath(EnsureTrailingSeparator(second)),
                StringComparison.OrdinalIgnoreCase);
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            _backgroundWorker?.CancelAsync();

            if (_log.HasWriteFailure)
            {
                MessageBox.Show($"Failed to write the log to:\n{_log.FilePath}", "ZipExtractor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
