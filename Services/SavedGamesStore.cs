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

        private static string NormalizeFolder(string folder)
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
