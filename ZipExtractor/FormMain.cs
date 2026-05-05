using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ZipExtractor.Properties;

namespace ZipExtractor
{
    public partial class FormMain : Form
    {
        private const int MaxRetries = 2;
        private BackgroundWorker _backgroundWorker;
        private readonly StringBuilder _logBuilder = new StringBuilder();

        private readonly string _zipFilePath;
        private readonly string _extractionPath;
        private readonly string _executablePath;
        private readonly string _executableArguments;
        private readonly bool _clearAppDirectory;
        private readonly ISet<string> _clearAppDirectoryIgnoreList;
        private readonly string _logFilePath;
        private readonly bool _exitOnComplete;

        public FormMain(
            string zipFilePath,
            string extractionPath,
            string executablePath,
            bool clearAppDirectory = false,
            IEnumerable<string> clearAppDirectoryIgnoreList = null,
            string executableArguments = null,
            string logFilePath = null,
            bool exitOnComplete = true)
        {
            InitializeComponent();
            _zipFilePath = zipFilePath;
            _extractionPath = extractionPath;
            _executablePath = executablePath;
            _clearAppDirectory = clearAppDirectory;
            _clearAppDirectoryIgnoreList = new HashSet<string>(
                clearAppDirectoryIgnoreList ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            _executableArguments = executableArguments;
            _logFilePath = logFilePath;
            _exitOnComplete = exitOnComplete;
        }

        public static FormMain FromCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();

            bool clearDir = args.Length > 4 && args[4].Equals("true", StringComparison.OrdinalIgnoreCase);

            IEnumerable<string> ignoreList = args.Length > 5 && !string.IsNullOrEmpty(args[5])
                ? args[5].Split('|').Select(s => s.Trim())
                : Enumerable.Empty<string>();

            string execArgs = args.Length > 6 ? args[6] : null;
            string logFilePath = args.Length > 7 && !string.IsNullOrEmpty(args[7]) ? args[7] : null;

            return new FormMain(
                args.Length > 1 ? args[1] : string.Empty,
                args.Length > 2 ? args[2] : string.Empty,
                args.Length > 3 ? args[3] : string.Empty,
                clearDir,
                ignoreList,
                execArgs,
                logFilePath);
        }

        private void FormMain_Shown(object sender, EventArgs e)
        {
            _logBuilder.AppendLine(DateTime.Now.ToString("F"));
            _logBuilder.AppendLine();
            _logBuilder.AppendLine("ZipExtractor started.");

            if (string.IsNullOrEmpty(_zipFilePath) || string.IsNullOrEmpty(_extractionPath) || string.IsNullOrEmpty(_executablePath))
            {
                MessageBox.Show("Missing required arguments.", "ZipExtractor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            _backgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            _backgroundWorker.DoWork += (_, eventArgs) =>
            {
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_executablePath)))
                {
                    try
                    {
                        if (process.MainModule is { FileName: { } } && process.MainModule.FileName.Equals(_executablePath))
                        {
                            _logBuilder.AppendLine("Waiting for application process to exit...");
                            _backgroundWorker.ReportProgress(0, "Waiting for application to exit...");
                            process.WaitForExit();
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine(exception.Message);
                    }
                }

                _logBuilder.AppendLine("BackgroundWorker started successfully.");

                var path = _extractionPath;

                // Ensures that the last character on the extraction path
                // is the directory separator char.
                // Without this, a malicious zip file could try to traverse outside of the expected
                // extraction path.
                if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    path += Path.DirectorySeparatorChar;
                }

                if (_clearAppDirectory)
                {
                    _logBuilder.AppendLine("Clearing application directory...");
                    _backgroundWorker.ReportProgress(0, "Clearing application directory...");

                    foreach (var file in Directory.GetFiles(path))
                    {
                        var fileName = Path.GetFileName(file);
                        if (!_clearAppDirectoryIgnoreList.Contains(fileName))
                        {
                            File.Delete(file);
                            _logBuilder.AppendLine($"Deleted file: {file}");
                        }
                        else
                        {
                            _logBuilder.AppendLine($"Skipped (ignored): {file}");
                        }
                    }

                    foreach (var directory in Directory.GetDirectories(path))
                    {
                        var dirName = Path.GetFileName(directory);
                        if (!_clearAppDirectoryIgnoreList.Contains(dirName))
                        {
                            Directory.Delete(directory, true);
                            _logBuilder.AppendLine($"Deleted directory: {directory}");
                        }
                        else
                        {
                            _logBuilder.AppendLine($"Skipped (ignored): {directory}");
                        }
                    }
                }

                var archive = ZipFile.OpenRead(_zipFilePath);
                var entries = archive.Entries;

                _logBuilder.AppendLine($"Found total of {entries.Count} files and folders inside the zip file.");

                try
                {
                    int progress = 0;
                    for (var index = 0; index < entries.Count; index++)
                    {
                        if (_backgroundWorker.CancellationPending)
                        {
                            eventArgs.Cancel = true;
                            break;
                        }

                        var entry = entries[index];

                        string currentFile = string.Format(Resources.CurrentFileExtracting, entry.FullName);
                        _backgroundWorker.ReportProgress(progress, currentFile);
                        int retries = 0;
                        bool notCopied = true;
                        while (notCopied)
                        {
                            string filePath = string.Empty;
                            try
                            {
                                filePath = Path.Combine(path, entry.FullName);
                                if (!entry.IsDirectory())
                                {
                                    var parentDirectory = Path.GetDirectoryName(filePath);
                                    if (!Directory.Exists(parentDirectory))
                                    {
                                        Directory.CreateDirectory(parentDirectory);
                                    }
                                    entry.ExtractToFile(filePath, true);
                                }
                                notCopied = false;
                            }
                            catch (IOException exception)
                            {
                                const int errorSharingViolation = 0x20;
                                const int errorLockViolation = 0x21;
                                var errorCode = Marshal.GetHRForException(exception) & 0x0000FFFF;
                                if (errorCode == errorSharingViolation || errorCode == errorLockViolation)
                                {
                                    retries++;
                                    if (retries > MaxRetries)
                                    {
                                        throw;
                                    }

                                    List<Process> lockingProcesses = null;
                                    if (Environment.OSVersion.Version.Major >= 6 && retries >= 2)
                                    {
                                        try
                                        {
                                            lockingProcesses = FileUtil.WhoIsLocking(filePath);
                                        }
                                        catch (Exception)
                                        {
                                            // ignored
                                        }
                                    }

                                    if (lockingProcesses == null)
                                    {
                                        Thread.Sleep(5000);
                                    }
                                    else
                                    {
                                        foreach (var lockingProcess in lockingProcesses)
                                        {
                                            var dialogResult = MessageBox.Show(
                                                string.Format(Resources.FileStillInUseMessage,
                                                    lockingProcess.ProcessName, filePath),
                                                Resources.FileStillInUseCaption,
                                                MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                                            if (dialogResult == DialogResult.Cancel)
                                            {
                                                throw;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }

                        progress = (index + 1) * 100 / entries.Count;
                        _backgroundWorker.ReportProgress(progress, currentFile);
                        _logBuilder.AppendLine($"{currentFile} [{progress}%]");
                    }
                }
                finally
                {
                    archive.Dispose();
                }
            };

            _backgroundWorker.ProgressChanged += (_, eventArgs) =>
            {
                progressBar.Value = eventArgs.ProgressPercentage;
                textBoxInformation.Text = eventArgs.UserState?.ToString();
                if (textBoxInformation.Text != null)
                {
                    textBoxInformation.SelectionStart = textBoxInformation.Text.Length;
                    textBoxInformation.SelectionLength = 0;
                }
            };

            _backgroundWorker.RunWorkerCompleted += (_, eventArgs) =>
            {
                try
                {
                    if (eventArgs.Error != null)
                    {
                        throw eventArgs.Error;
                    }

                    if (!eventArgs.Cancelled)
                    {
                        textBoxInformation.Text = @"Finished";
                        try
                        {
                            var processStartInfo = new ProcessStartInfo(_executablePath);
                            if (!string.IsNullOrEmpty(_executableArguments))
                            {
                                processStartInfo.Arguments = _executableArguments;
                            }

                            Process.Start(processStartInfo);
                            _logBuilder.AppendLine("Successfully launched the updated application.");
                        }
                        catch (Win32Exception exception)
                        {
                            if (exception.NativeErrorCode != 1223)
                            {
                                throw;
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logBuilder.AppendLine();
                    _logBuilder.AppendLine(exception.ToString());
                    MessageBox.Show(exception.Message, exception.GetType().ToString(),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _logBuilder.AppendLine();
                    if (_exitOnComplete)
                    {
                        Application.Exit();
                    }
                    else
                    {
                        Close();
                    }
                }
            };

            _backgroundWorker.RunWorkerAsync();
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            _backgroundWorker?.CancelAsync();

            _logBuilder.AppendLine();
            string logPath = !string.IsNullOrEmpty(_logFilePath)
                ? _logFilePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZipExtractor.log");

            try
            {
                var logDir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                File.WriteAllText(logPath, _logBuilder.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to write log to:\n{logPath}\n\n{ex.Message}",
                    "ZipExtractor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
