using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace dlss_updater.Services
{
    internal static class BackupScanner
    {
        private static readonly Regex BackupFileNamePattern =
            new Regex(
                @"^nvngx(?:_dlss(?:d|g)?)?\.dll\.(?<version>.+)\.dubackup\.zip$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static List<ScannedFile> FindBackupFiles(
            string folderPath,
            ICollection<string> warningPaths = null)
        {
            List<ScannedFile> results =
                new List<ScannedFile>();

            if (string.IsNullOrEmpty(folderPath) ||
                !Directory.Exists(folderPath))
            {
                return results;
            }

            FolderScanner.Walk(folderPath, "nvngx*.dubackup.zip", file =>
            {
                string version;
                if (TryGetBackupVersion(Path.GetFileName(file), out version))
                {
                    results.Add(new ScannedFile { Path = file, Version = version });
                }
            }, warningPaths);

            Remember(results);
            return results;
        }

        public static bool IsBackupFileName(string fileName)
        {
            return !string.IsNullOrEmpty(fileName) &&
                BackupFileNamePattern.IsMatch(fileName);
        }

        public static bool TryGetBackupVersion(string fileName, out string version)
        {
            version = null;

            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            Match match = BackupFileNamePattern.Match(fileName);
            if (!match.Success)
            {
                return false;
            }

            version = match.Groups["version"].Value;
            return true;
        }

        // Remember them for File > Delete all backups, including
        // backups made by older versions of the app.
        internal static void Remember(IList<ScannedFile> backups)
        {
            if (backups.Count > 0)
            {
                BackupIndexStore.Record(backups.Select(file => file.Path));
            }
        }
    }
}
