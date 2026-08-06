using System;
using System.IO;

namespace ZipExtractor
{
    /// <summary>
    ///     Removes everything from the installation directory except the entries that must survive. Walks subdirectories
    ///     rather than deleting them wholesale so a protected file nested inside one is not taken down with it, and treats
    ///     every deletion failure as recoverable: extracting the update matters more than removing a stale file.
    /// </summary>
    public class DirectoryCleaner
    {
        private readonly PreservedEntries _preserved;
        private readonly UpdateLog _log;

        public DirectoryCleaner(PreservedEntries preserved, UpdateLog log)
        {
            _preserved = preserved;
            _log = log;
        }

        public void Clear(string directoryPath)
        {
            ClearRecursively(directoryPath);
        }

        private bool ClearRecursively(string directoryPath)
        {
            var isEmpty = true;

            foreach (var filePath in GetFiles(directoryPath))
            {
                if (!DeleteFile(filePath))
                {
                    isEmpty = false;
                }
            }

            foreach (var subdirectoryPath in GetDirectories(directoryPath))
            {
                if (!DeleteDirectory(subdirectoryPath))
                {
                    isEmpty = false;
                }
            }

            return isEmpty;
        }

        private bool DeleteFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            if (_preserved.IsPreserved(fileName))
            {
                _log.Write($"Skipped (preserved): {filePath}");
                return false;
            }

            try
            {
                File.Delete(filePath);
                _log.Write($"Deleted file: {filePath}");
                return true;
            }
            catch (Exception exception)
            {
                _log.Write($"Skipped ({DescribeFailure(exception)}): {filePath}");
                return false;
            }
        }

        private bool DeleteDirectory(string directoryPath)
        {
            var directoryName = Path.GetFileName(directoryPath);
            if (_preserved.IsPreserved(directoryName))
            {
                _log.Write($"Skipped (preserved): {directoryPath}");
                return false;
            }

            if (!ClearRecursively(directoryPath))
            {
                return false;
            }

            try
            {
                Directory.Delete(directoryPath, false);
                _log.Write($"Deleted directory: {directoryPath}");
                return true;
            }
            catch (Exception exception)
            {
                _log.Write($"Skipped ({DescribeFailure(exception)}): {directoryPath}");
                return false;
            }
        }

        private string[] GetFiles(string directoryPath)
        {
            try
            {
                return Directory.GetFiles(directoryPath);
            }
            catch (Exception exception)
            {
                _log.WriteException($"Failed to list files in {directoryPath}", exception);
                return new string[0];
            }
        }

        private string[] GetDirectories(string directoryPath)
        {
            try
            {
                return Directory.GetDirectories(directoryPath);
            }
            catch (Exception exception)
            {
                _log.WriteException($"Failed to list subdirectories of {directoryPath}", exception);
                return new string[0];
            }
        }

        private static string DescribeFailure(Exception exception)
        {
            return exception is UnauthorizedAccessException ? "access denied" : "in use";
        }
    }
}
