using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace dlss_updater.Services
{
    [DataContract]
    internal sealed class SavedGameFile
    {
        [DataMember(Name = "path")]
        public string Path { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }
    }

    [DataContract]
    internal sealed class SavedGame
    {
        [DataMember(Name = "folder")]
        public string Folder { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "lastScannedUtc")]
        public string LastScannedUtc { get; set; }

        [DataMember(Name = "files")]
        public List<SavedGameFile> Files { get; set; }
    }

    [DataContract]
    internal sealed class SavedState
    {
        [DataMember(Name = "games")]
        public List<SavedGame> Games { get; set; }

        // Manifest versions the user has already been told about, so the
        // startup banner only appears when something new was published.
        [DataMember(Name = "seenVersions")]
        public Dictionary<string, string> SeenVersions { get; set; }
    }

    internal sealed class SavedFolderSnapshot
    {
        public string Folder { get; set; }

        public string LastScannedUtc { get; set; }

        public List<string> Paths { get; set; }
    }

    internal sealed class SavedFoldersDiskState
    {
        // Normalized folder paths (see SavedGamesStore.NormalizeFolder).
        public HashSet<string> MissingFolders { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Folder -> its LastScannedUtc when the snapshot was taken. A folder
        // scanned again since then already has fresher data, so the result
        // is not applied to it.
        public Dictionary<string, string> Stamps { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // DLL path -> version, for the saved DLLs that still exist.
        public Dictionary<string, string> FileVersions { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    // Saved games and seen manifest versions, kept in
    // %LocalAppData%\DLSS Updater\saved-games.json.
    internal static class SavedGamesStore
    {
        private static SavedState _state;

        public static IList<SavedGame> Games
        {
            get { return State.Games; }
        }

        public static IDictionary<string, string> SeenVersions
        {
            get { return State.SeenVersions; }
        }

        // The "Your folders" list never opens on its own: it starts collapsed
        // every launch and only opens when the user clicks it.
        public static bool FoldersExpanded { get; set; }

        private static SavedState State
        {
            get
            {
                if (_state == null)
                {
                    _state = Load();
                }

                return _state;
            }
        }

        // Records the DLSS files found in a folder. Scanning only refreshes
        // folders that are already saved; a folder is added to the list when
        // the user updates it (addIfMissing). Returns true if it was added.
        public static bool RecordScan(string folder, IEnumerable<ScannedFile> files, bool addIfMissing)
        {
            List<SavedGameFile> savedFiles = files
                .Where(file => file != null && !string.IsNullOrWhiteSpace(file.Path))
                .Select(file => new SavedGameFile { Path = file.Path, Version = file.Version })
                .ToList();

            if (savedFiles.Count == 0)
            {
                return false;
            }

            SavedGame game = Find(folder);
            bool added = game == null;
            if (added && !addIfMissing)
            {
                return false;
            }

            if (added)
            {
                game = new SavedGame
                {
                    Folder = folder,
                    Name = GetDisplayName(folder)
                };
            }
            else
            {
                State.Games.Remove(game);
            }

            game.Files = savedFiles;
            game.LastScannedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            State.Games.Insert(0, game);
            Save();
            return added;
        }

        // A copy of every saved folder and its DLL paths, taken on the UI
        // thread so ReadDiskState can run on another one.
        public static List<SavedFolderSnapshot> GetFolderSnapshot()
        {
            return State.Games
                .Select(game => new SavedFolderSnapshot
                {
                    Folder = game.Folder,
                    LastScannedUtc = game.LastScannedUtc,
                    Paths = game.Files.Select(file => file.Path).ToList()
                })
                .ToList();
        }

        // Reads which saved folders exist and the version of every saved DLL.
        // Touches only the disk, never the saved state, so it can run off the
        // UI thread: a folder on a sleeping drive or an unreachable network
        // share can take many seconds to answer.
        public static SavedFoldersDiskState ReadDiskState(
            IEnumerable<SavedFolderSnapshot> snapshot)
        {
            SavedFoldersDiskState state = new SavedFoldersDiskState();

            foreach (SavedFolderSnapshot folder in snapshot)
            {
                string key = NormalizeFolder(folder.Folder);
                state.Stamps[key] = folder.LastScannedUtc ?? string.Empty;

                if (!Directory.Exists(folder.Folder))
                {
                    state.MissingFolders.Add(key);
                    continue;
                }

                foreach (string path in folder.Paths)
                {
                    if (File.Exists(path))
                    {
                        state.FileVersions[path] = FolderScanner.GetFileVersion(path);
                    }
                }
            }

            return state;
        }

        // Applies ReadDiskState's result. A game patch or a Steam "verify
        // files" can put an older DLL back after we updated it, and the list
        // should say so instead of showing the old result. Files that no
        // longer exist are dropped.
        public static void ApplyDiskState(SavedFoldersDiskState state)
        {
            bool changed = false;

            foreach (SavedGame game in State.Games)
            {
                if (!IsUnchangedSince(state, game) ||
                    state.MissingFolders.Contains(NormalizeFolder(game.Folder)))
                {
                    continue;
                }

                List<SavedGameFile> current = new List<SavedGameFile>();

                foreach (SavedGameFile file in game.Files)
                {
                    string version;
                    if (!state.FileVersions.TryGetValue(file.Path, out version))
                    {
                        changed = true;
                        continue;
                    }

                    if (!string.Equals(version, file.Version, StringComparison.Ordinal))
                    {
                        file.Version = version;
                        changed = true;
                    }

                    current.Add(file);
                }

                game.Files = current;
            }

            if (changed)
            {
                Save();
            }
        }

        // The saved folders ReadDiskState found missing, leaving out any
        // that were scanned (so exist) after the snapshot was taken.
        public static HashSet<string> GetMissingFolders(SavedFoldersDiskState state)
        {
            HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SavedGame game in State.Games)
            {
                string key = NormalizeFolder(game.Folder);
                if (state.MissingFolders.Contains(key) && IsUnchangedSince(state, game))
                {
                    missing.Add(key);
                }
            }

            return missing;
        }

        private static bool IsUnchangedSince(SavedFoldersDiskState state, SavedGame game)
        {
            string stamp;
            return state.Stamps.TryGetValue(NormalizeFolder(game.Folder), out stamp) &&
                string.Equals(stamp, game.LastScannedUtc ?? string.Empty, StringComparison.Ordinal);
        }

        public static void Remove(string folder)
        {
            SavedGame game = Find(folder);
            if (game == null)
            {
                return;
            }

            State.Games.Remove(game);
            Save();
        }

        public static void SetSeenVersions(IDictionary<string, string> versions)
        {
            if (versions == null || versions.Count == 0)
            {
                return;
            }

            State.SeenVersions = new Dictionary<string, string>(
                versions,
                StringComparer.OrdinalIgnoreCase);
            Save();
        }

        public static SavedGame Find(string folder)
        {
            string key = NormalizeFolder(folder);
            return State.Games.FirstOrDefault(game =>
                string.Equals(NormalizeFolder(game.Folder), key, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDisplayName(string folder)
        {
            string trimmed = NormalizeFolder(folder);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? trimmed : name;
        }

        internal static string NormalizeFolder(string folder)
        {
            return (folder ?? string.Empty).Trim().TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static string GetStatePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DLSS Updater",
                "saved-games.json");
        }

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(
                typeof(SavedState),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
        }

        private static SavedState Load()
        {
            SavedState state = null;

            try
            {
                string path = GetStatePath();
                if (File.Exists(path))
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        state = (SavedState)CreateSerializer().ReadObject(stream);
                    }
                }
            }
            catch
            {
                // A damaged file shouldn't stop the app from starting.
                state = null;
            }

            if (state == null)
            {
                state = new SavedState();
            }

            state.Games = (state.Games ?? new List<SavedGame>())
                .Where(game => game != null && !string.IsNullOrWhiteSpace(game.Folder))
                .ToList();

            foreach (SavedGame game in state.Games)
            {
                if (game.Files == null)
                {
                    game.Files = new List<SavedGameFile>();
                }

                if (string.IsNullOrWhiteSpace(game.Name))
                {
                    game.Name = GetDisplayName(game.Folder);
                }
            }

            state.SeenVersions = state.SeenVersions == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(state.SeenVersions, StringComparer.OrdinalIgnoreCase);

            return state;
        }

        private static void Save()
        {
            try
            {
                string path = GetStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporaryPath = path + ".tmp";

                using (FileStream stream = File.Create(temporaryPath))
                {
                    CreateSerializer().WriteObject(stream, State);
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
                // Saving is best effort; the list still works for this session.
            }
        }
    }
}
