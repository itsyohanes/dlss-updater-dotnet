using System.Collections.Generic;
using dlss_updater.Services;
using Xunit;

namespace DlssUpdater.Tests
{
    // ReadDiskState only reads the disk, so it can be tested without
    // touching %LOCALAPPDATA%\DLSS Updater\saved-games.json.
    public class SavedFoldersDiskStateTests
    {
        [Fact]
        public void ReadDiskState_ReportsMissingFoldersAndExistingFiles()
        {
            using (TempFolder folder = new TempFolder())
            {
                string present = folder.WriteFile(@"game\nvngx_dlss.dll", "x");
                string removed = folder.Combine("game", "nvngx_dlssg.dll");
                string missingFolder = folder.Combine("gone");

                SavedFoldersDiskState state = SavedGamesStore.ReadDiskState(new List<SavedFolderSnapshot>
                {
                    new SavedFolderSnapshot
                    {
                        Folder = folder.Combine("game") + "\\",
                        LastScannedUtc = "a",
                        Paths = new List<string> { present, removed }
                    },
                    new SavedFolderSnapshot
                    {
                        Folder = missingFolder,
                        LastScannedUtc = "b",
                        Paths = new List<string> { missingFolder + @"\nvngx_dlss.dll" }
                    }
                });

                Assert.Equal(new[] { missingFolder }, state.MissingFolders);
                Assert.Equal("Unknown", state.FileVersions[present]);
                Assert.False(state.FileVersions.ContainsKey(removed));
                Assert.Equal("a", state.Stamps[folder.Combine("game")]);
                Assert.Equal("b", state.Stamps[missingFolder]);
            }
        }
    }
}
