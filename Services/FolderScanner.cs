using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace dlss_updater.Services
{
    internal class ScannedFile
    {
        public string Path { get; set; }

        public string Version { get; set; }
    }

    // Everything one walk of a folder found.
    internal sealed class FolderScanResult
    {
        public List<ScannedFile> DlssFiles { get; } = new List<ScannedFile>();

        public List<ScannedFile> DlssdFiles { get; } = new List<ScannedFile>();

        public List<ScannedFile> DlssgFiles { get; } = new List<ScannedFile>();

        public List<ScannedFile> BackupFiles { get; } = new List<ScannedFile>();

        public List<string> WarningPaths { get; } = new List<string>();
    }

    internal static class FolderScanner
    {
        // DLSS DLLs and DLSS Updater backups all start with this.
        private const string ScanPattern = "nvngx*";

        // Finds every DLSS file and backup under folderPath in one walk of
        // the folder tree, instead of one walk per file name.
        public static FolderScanResult Scan(string folderPath)
        {
            FolderScanResult result = new FolderScanResult();

            if (string.IsNullOrEmpty(folderPath) ||
                !Directory.Exists(folderPath))
            {
                return result;
            }

            Walk(folderPath, ScanPattern, file =>
            {
                string fileName = Path.GetFileName(file);
                string backupVersion;

                if (string.Equals(fileName, "nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase))
                {
                    result.DlssFiles.Add(ReadFile(file));
                }
                else if (string.Equals(fileName, "nvngx_dlssd.dll", StringComparison.OrdinalIgnoreCase))
                {
                    result.DlssdFiles.Add(ReadFile(file));
                }
                else if (string.Equals(fileName, "nvngx_dlssg.dll", StringComparison.OrdinalIgnoreCase))
                {
                    result.DlssgFiles.Add(ReadFile(file));
                }
                else if (BackupScanner.TryGetBackupVersion(fileName, out backupVersion))
                {
                    result.BackupFiles.Add(new ScannedFile { Path = file, Version = backupVersion });
                }
            }, result.WarningPaths);

            BackupScanner.Remember(result.BackupFiles);
            return result;
        }

        public static List<ScannedFile> FindFiles(
            string folderPath,
            string fileName,
            ICollection<string> warningPaths = null)
        {
            List<ScannedFile> results =
                new List<ScannedFile>();

            if (string.IsNullOrEmpty(folderPath) ||
                !Directory.Exists(folderPath))
            {
                return results;
            }

            Walk(folderPath, fileName, file => results.Add(ReadFile(file)), warningPaths);
            return results;
        }

        // Visits every file matching pattern under root. Junctions and
        // symbolic links below root are not followed: they can point back
        // up the tree (an endless walk), outside the chosen folder, or at a
        // folder that is also reached another way (the same DLL twice).
        // Folders that can't be read are added to warningPaths.
        internal static void Walk(
            string root,
            string pattern,
            Action<string> onFile,
            ICollection<string> warningPaths)
        {
            HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string folder = pending.Pop();

                try
                {
                    foreach (string file in Directory.GetFiles(folder, pattern))
                    {
                        onFile(file);
                    }
                }
                catch
                {
                    if (warned.Add(folder))
                    {
                        warningPaths?.Add(folder);
                    }
                }

                DirectoryInfo[] subfolders;

                try
                {
                    subfolders = new DirectoryInfo(folder).GetDirectories();
                }
                catch
                {
                    if (warned.Add(folder))
                    {
                        warningPaths?.Add(folder);
                    }

                    continue;
                }

                // Pushed in reverse so folders are visited in listing order.
                foreach (DirectoryInfo subfolder in subfolders.Reverse())
                {
                    if ((subfolder.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(subfolder.FullName);
                    }
                }
            }
        }

        private static ScannedFile ReadFile(string path)
        {
            return new ScannedFile
            {
                Path = path,
                Version = GetFileVersion(path)
            };
        }

        public static string GetFileVersion(
            string filePath)
        {
            try
            {
                FileVersionInfo versionInfo =
                    FileVersionInfo.GetVersionInfo(
                        filePath);

                if (!string.IsNullOrEmpty(
                    versionInfo.FileVersion))
                {
                    return versionInfo.FileVersion.Replace(",", ".");
                }
            }
            catch
            {
            }

            return "Unknown";
        }
    }
}
