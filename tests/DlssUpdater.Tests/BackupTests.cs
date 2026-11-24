using System.Collections.Generic;
using System.IO;
using dlss_updater.Services;
using Xunit;

namespace DlssUpdater.Tests
{
    // Only covers code that stays inside a temp folder. Creating backups and
    // scanning for them also writes %LOCALAPPDATA%\DLSS Updater\backups.json,
    // so those paths are left out to keep test runs off the real index.
    public class BackupTests
    {
        [Theory]
        [InlineData("nvngx_dlss.dll.310.9.16.0.dubackup.zip")]
        [InlineData("nvngx_dlssg.dll.310.9.16.0.dubackup.zip")]
        [InlineData("nvngx_dlssd.dll.310.9.16.0.dubackup.zip")]
        [InlineData("nvngx.dll.1.0.0.0.dubackup.zip")]
        [InlineData("NVNGX_DLSS.DLL.2.5.1.DUBACKUP.ZIP")]
        public void IsBackupFileName_AcceptsBackupsOfSupportedDlls(string fileName)
        {
            Assert.True(BackupScanner.IsBackupFileName(fileName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nvngx_dlss.dll")]
        [InlineData("nvngx_dlss.dll.dubackup.zip")]
        [InlineData("nvngx_dlssx.dll.1.0.dubackup.zip")]
        [InlineData("other.dll.1.0.dubackup.zip")]
        [InlineData("nvngx_dlss.dll.1.0.zip")]
        public void IsBackupFileName_RejectsEverythingElse(string fileName)
        {
            Assert.False(BackupScanner.IsBackupFileName(fileName));
        }

        [Theory]
        [InlineData("nvngx_dlss.dll.310.9.16.0.dubackup.zip", "310.9.16.0")]
        [InlineData("NVNGX_DLSSG.DLL.2.5.1.DUBACKUP.ZIP", "2.5.1")]
        public void TryGetBackupVersion_ReadsTheVersionFromTheName(string fileName, string expected)
        {
            string version;

            Assert.True(BackupScanner.TryGetBackupVersion(fileName, out version));
            Assert.Equal(expected, version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("nvngx_dlss.dll")]
        public void TryGetBackupVersion_RejectsOtherFiles(string fileName)
        {
            string version;

            Assert.False(BackupScanner.TryGetBackupVersion(fileName, out version));
            Assert.Null(version);
        }

        [Fact]
        public void RestoreLatestBackups_RestoresNewestBackupAndDeletesIt()
        {
            using (TempFolder folder = new TempFolder())
            {
                string dll = folder.WriteFile("nvngx_dlss.dll", "updated");
                string older = folder.WriteZip("nvngx_dlss.dll.2.5.1.0.dubackup.zip", "nvngx_dlss.dll", "2.5.1");
                string newer = folder.WriteZip("nvngx_dlss.dll.3.7.10.0.dubackup.zip", "nvngx_dlss.dll", "3.7.10");

                BackupOperationResult result = BackupArchiveService.RestoreLatestBackups(
                    new List<ScannedFile>
                    {
                        new ScannedFile { Path = older },
                        new ScannedFile { Path = newer }
                    });

                Assert.Empty(result.Errors);
                Assert.Equal(1, result.RestoredFiles);
                Assert.Equal(1, result.DeletedFiles);
                Assert.Equal("3.7.10", File.ReadAllText(dll));
                Assert.False(File.Exists(newer));
                Assert.True(File.Exists(older));
                Assert.Equal(2, Directory.GetFiles(folder.Path).Length);
            }
        }

        [Fact]
        public void RestoreLatestBackups_RestoresIntoFolderWithoutTheDll()
        {
            using (TempFolder folder = new TempFolder())
            {
                string backup = folder.WriteZip(@"sub\nvngx_dlssg.dll.1.0.7.0.dubackup.zip", "nvngx_dlssg.dll", "fg");

                BackupOperationResult result = BackupArchiveService.RestoreLatestBackups(
                    new List<ScannedFile> { new ScannedFile { Path = backup } });

                Assert.Empty(result.Errors);
                Assert.Equal("fg", File.ReadAllText(folder.Combine("sub", "nvngx_dlssg.dll")));
            }
        }

        [Fact]
        public void RestoreLatestBackups_LeavesDllAloneWhenBackupHasWrongEntry()
        {
            using (TempFolder folder = new TempFolder())
            {
                string dll = folder.WriteFile("nvngx_dlss.dll", "updated");
                string backup = folder.WriteZip("nvngx_dlss.dll.2.5.1.0.dubackup.zip", "something-else.dll", "x");

                BackupOperationResult result = BackupArchiveService.RestoreLatestBackups(
                    new List<ScannedFile> { new ScannedFile { Path = backup } });

                Assert.Single(result.Errors);
                Assert.Equal(0, result.RestoredFiles);
                Assert.Equal("updated", File.ReadAllText(dll));
                Assert.True(File.Exists(backup));
                // No staging files left behind.
                Assert.Equal(2, Directory.GetFiles(folder.Path).Length);
            }
        }

        [Fact]
        public void RestoreLatestBackups_ReportsUnrecognizedAndUnreadableBackups()
        {
            using (TempFolder folder = new TempFolder())
            {
                string unrecognized = folder.WriteZip("random.zip", "nvngx_dlss.dll", "x");
                string badVersion = folder.WriteZip("nvngx_dlss.dll.abc.dubackup.zip", "nvngx_dlss.dll", "x");

                BackupOperationResult result = BackupArchiveService.RestoreLatestBackups(
                    new List<ScannedFile>
                    {
                        new ScannedFile { Path = unrecognized },
                        new ScannedFile { Path = badVersion }
                    });

                Assert.Equal(2, result.Errors.Count);
                Assert.Equal(0, result.RestoredFiles);
                Assert.False(File.Exists(folder.Combine("nvngx_dlss.dll")));
            }
        }

        [Fact]
        public void RestoreAndDelete_ReportNullSelection()
        {
            Assert.Single(BackupArchiveService.RestoreLatestBackups(null).Errors);
            Assert.Single(BackupArchiveService.DeleteBackups(null).Errors);
        }

        [Fact]
        public void DeleteBackups_DeletesEachFile()
        {
            using (TempFolder folder = new TempFolder())
            {
                string first = folder.WriteZip("nvngx_dlss.dll.1.0.dubackup.zip", "nvngx_dlss.dll", "a");
                string second = folder.WriteZip(@"sub\nvngx_dlssd.dll.1.0.dubackup.zip", "nvngx_dlssd.dll", "b");

                BackupOperationResult result = BackupArchiveService.DeleteBackups(
                    new List<ScannedFile>
                    {
                        new ScannedFile { Path = first },
                        new ScannedFile { Path = second }
                    });

                Assert.Empty(result.Errors);
                Assert.Equal(2, result.DeletedFiles);
                Assert.False(File.Exists(first));
                Assert.False(File.Exists(second));
            }
        }
    }
}
