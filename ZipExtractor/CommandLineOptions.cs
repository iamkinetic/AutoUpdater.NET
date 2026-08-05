using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ZipExtractor
{
    /// <summary>
    ///     Named arguments handed over by AutoUpdater.NET. Named rather than positional because the positional scheme this
    ///     replaced shifted the host application arguments once already, and every new parameter moved them again.
    /// </summary>
    public class CommandLineOptions
    {
        public string ZipFilePath { get; set; } = string.Empty;

        public string ExtractionPath { get; set; } = string.Empty;

        public string ExecutablePath { get; set; } = string.Empty;

        public string ExecutableArguments { get; set; }

        public string LogFilePath { get; set; }

        public int? ApplicationProcessId { get; set; }

        public bool ClearAppDirectory { get; set; }

        public bool ForceCloseApplication { get; set; }

        public TimeSpan ForceCloseTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public IEnumerable<string> IgnoredNames { get; set; } = Enumerable.Empty<string>();

        public IEnumerable<string> ProtectedPatterns { get; set; } = Enumerable.Empty<string>();

        public bool IsValid =>
            !string.IsNullOrEmpty(ZipFilePath) &&
            !string.IsNullOrEmpty(ExtractionPath) &&
            !string.IsNullOrEmpty(ExecutablePath);

        public static CommandLineOptions Parse(IEnumerable<string> arguments)
        {
            var options = new CommandLineOptions();

            foreach (var argument in arguments)
            {
                if (argument == null || !argument.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = argument.IndexOf('=');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var name = argument.Substring(2, separatorIndex - 2);
                var value = argument.Substring(separatorIndex + 1);
                options.Apply(name, value);
            }

            return options;
        }

        private void Apply(string name, string value)
        {
            switch (name.ToLowerInvariant())
            {
                case "zip":
                    ZipFilePath = value;
                    break;
                case "out":
                    ExtractionPath = value;
                    break;
                case "exe":
                    ExecutablePath = value;
                    break;
                case "pid":
                    ApplicationProcessId = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var processId) && processId > 0
                        ? processId
                        : (int?) null;
                    break;
                case "clear":
                    ClearAppDirectory = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "keep":
                    IgnoredNames = SplitList(value);
                    break;
                case "keep-patterns":
                    ProtectedPatterns = SplitList(value);
                    break;
                case "force-close":
                    ForceCloseApplication = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "force-close-timeout":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
                        seconds >= 0)
                    {
                        ForceCloseTimeout = TimeSpan.FromSeconds(seconds);
                    }

                    break;
                case "log":
                    LogFilePath = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "app-args":
                    ExecutableArguments = string.IsNullOrEmpty(value) ? null : value;
                    break;
            }
        }

        private static IEnumerable<string> SplitList(string value)
        {
            return string.IsNullOrEmpty(value)
                ? Enumerable.Empty<string>()
                : value.Split('|')
                    .Select(entry => entry.Trim())
                    .Where(entry => entry.Length > 0)
                    .ToList();
        }
    }
}
