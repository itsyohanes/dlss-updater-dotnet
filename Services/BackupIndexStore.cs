using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace dlss_updater.Services
{
    [DataContract]
    internal sealed class BackupIndex
    {
        [DataMember(Name = "backups")]
        public List<string> Backups { get; set; }
    }

    // Every DLSS backup (*.dubackup.zip) the app has made or found in a
    // scanned folder, kept in %LOCALAPPDATA%\DLSS Updater\backups.json so
    // File > Delete all backups can find them without searching drives.
    // The file is re-read on every change, so several copies of the app
    // add to the same list instead of overwriting each other.
    internal static class BackupIndexStore
    {
        private static readonly object Sync = new object();

        public static void Record(IEnumerable<string> backupPaths)
        {
            lock (Sync)
            {
                List<string> backups = Load();
                HashSet<string> known = new HashSet<string>(backups, StringComparer.OrdinalIgnoreCase);
                bool changed = false;

                foreach (string path in backupPaths ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    string fullPath = Path.GetFullPath(path);
                    if (known.Add(fullPath))
                    {
                        backups.Add(fullPath);
                        changed = true;
                    }
                }

                if (changed)
                {
                    Save(backups);
                }
            }
        }

        // Backups that still exist and still look like DLSS Updater backups.
        // Anything else is dropped from the list.
        public static List<string> GetExistingBackups()
        {
            lock (Sync)
            {
                List<string> backups = Load();
                List<string> existing = backups
                    .Where(path => BackupScanner.IsBackupFileName(Path.GetFileName(path)) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (existing.Count != backups.Count)
                {
                    Save(existing);
                }

                return existing;
            }
        }

        private static string GetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DLSS Updater",
                "backups.json");
        }

        private static List<string> Load()
        {
            try
            {
                string path = GetPath();
                if (File.Exists(path))
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        BackupIndex index = (BackupIndex)new DataContractJsonSerializer(typeof(BackupIndex)).ReadObject(stream);
                        if (index != null && index.Backups != null)
                        {
                            return index.Backups.Where(entry => !string.IsNullOrWhiteSpace(entry)).ToList();
                        }
                    }
                }
            }
            catch
            {
                // Missing or unreadable: start a new list.
            }

            return new List<string>();
        }

        private static void Save(List<string> backups)
        {
            try
            {
                string path = GetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporaryPath = path + ".tmp";

                using (FileStream stream = File.Create(temporaryPath))
                {
                    new DataContractJsonSerializer(typeof(BackupIndex)).WriteObject(
                        stream,
                        new BackupIndex { Backups = backups });
                }

                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            catch
            {
                // Best effort; the backups themselves are unaffected.
            }
        }
    }
}
