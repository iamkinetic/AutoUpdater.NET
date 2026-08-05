using System;
using System.Globalization;
using System.IO;

namespace ZipExtractor
{
    /// <summary>
    ///     Appends every entry to disk as it happens. A failed update often ends with the process being killed or hanging
    ///     on a locked file, and buffering the log until the form closes loses exactly the run worth diagnosing.
    /// </summary>
    public class UpdateLog
    {
        private const string PreviousLogSuffix = ".previous.log";

        private readonly object _writeLock = new object();
        private readonly string _logFilePath;
        private bool _writeFailed;

        public UpdateLog(string logFilePath)
        {
            _logFilePath = string.IsNullOrEmpty(logFilePath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZipExtractor.log")
                : logFilePath;

            Prepare();
        }

        public string FilePath => _logFilePath;

        public bool HasWriteFailure => _writeFailed;

        public void Write(string message)
        {
            lock (_writeLock)
            {
                try
                {
                    File.AppendAllText(_logFilePath,
                        $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {message}{Environment.NewLine}");
                }
                catch (Exception)
                {
                    _writeFailed = true;
                }
            }
        }

        public void WriteException(string context, Exception exception)
        {
            Write($"{context}: {exception.GetType().Name}: {exception.Message}");
        }

        private void Prepare()
        {
            try
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                RollPreviousLog();
            }
            catch (Exception)
            {
                _writeFailed = true;
                return;
            }

            Write($"ZipExtractor started. {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}");
        }

        /// <summary>
        ///     Keeps a single previous generation. The run that broke the installation is the one you need to read, and it is
        ///     always the run before the one currently starting.
        /// </summary>
        private void RollPreviousLog()
        {
            if (!File.Exists(_logFilePath))
            {
                return;
            }

            var previousLogPath = _logFilePath + PreviousLogSuffix;
            if (File.Exists(previousLogPath))
            {
                File.Delete(previousLogPath);
            }

            File.Move(_logFilePath, previousLogPath);
        }
    }
}
