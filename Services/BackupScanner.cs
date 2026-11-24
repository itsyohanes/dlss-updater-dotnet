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

            SearchDirectory(
                folderPath,
                results,
                warningPaths);

            // Remember them for File > Delete all backups, including
            // backups made by older versions of the app.
            if (results.Count > 0)
            {
                BackupIndexStore.Record(results.Select(file => file.Path));
            }

            return results;
        }

        public static bool IsBackupFileName(string fileName)
        {
            return !string.IsNullOrEmpty(fileName) &&
                BackupFileNamePattern.IsMatch(fileName);
        }

        private static void SearchDirectory(
            string folderPath,
            List<ScannedFile> results,
            ICollection<string> warningPaths)
        {
            try
            {
                foreach (string file in Directory.GetFiles(
                    folderPath,
                    "nvngx*.dubackup.zip"))
                {
                    Match match = BackupFileNamePattern.Match(
                        Path.GetFileName(file));

                    if (match.Success)
                    {
                        results.Add(
                            new ScannedFile
                            {
                                Path = file,
                                Version = match.Groups["version"].Value
                            });
                    }
                }
            }
            catch
            {
                warningPaths?.Add(folderPath);
            }

            string[] directories;

            try
            {
                directories = Directory.GetDirectories(folderPath);
            }
            catch
            {
                warningPaths?.Add(folderPath);
                return;
            }

            foreach (string directory in directories)
            {
                SearchDirectory(
                    directory,
                    results,
                    warningPaths);
            }
        }
    }
}
