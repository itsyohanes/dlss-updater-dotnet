using System.Diagnostics;
using System.IO;
using System.Linq;
using dlss_updater.Services;
using Xunit;

namespace DlssUpdater.Tests
{
    public class FolderScannerTests
    {
        [Fact]
        public void FindFiles_SearchesSubfoldersAndReadsFileVersion()
        {
            using (TempFolder folder = new TempFolder())
            {
                // Any DLL with version info will do; this assembly has one.
                string source = typeof(FolderScannerTests).Assembly.Location;
                string nested = folder.Combine("a", "b", "nvngx_dlss.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(nested));
                File.Copy(source, nested);
                string noVersion = folder.WriteFile("nvngx_dlss.dll", "not a real dll");
                folder.WriteFile(@"a\nvngx_dlssg.dll", "other file");

                var results = FolderScanner.FindFiles(folder.Path, "nvngx_dlss.dll")
                    .OrderBy(file => file.Path.Length)
                    .ToList();

                Assert.Equal(2, results.Count);
                Assert.Equal(noVersion, results[0].Path);
                Assert.Equal("Unknown", results[0].Version);
                Assert.Equal(nested, results[1].Path);
                Assert.Equal(
                    FileVersionInfo.GetVersionInfo(source).FileVersion,
                    results[1].Version);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(@"Z:\this\folder\does\not\exist")]
        public void FindFiles_MissingFolderReturnsNothing(string folder)
        {
            Assert.Empty(FolderScanner.FindFiles(folder, "nvngx_dlss.dll"));
        }

        [Fact]
        public void Scan_FindsEveryDlssTypeInOneWalk()
        {
            using (TempFolder folder = new TempFolder())
            {
                string dlss = folder.WriteFile(@"game\nvngx_dlss.dll", "x");
                string dlssd = folder.WriteFile(@"game\bin\NVNGX_DLSSD.DLL", "x");
                string dlssg = folder.WriteFile(@"other\nvngx_dlssg.dll", "x");
                folder.WriteFile(@"game\nvngx_dlss.dll.txt", "not a DLSS file");
                folder.WriteFile(@"game\nvngx_other.dll", "not a DLSS file");

                FolderScanResult result = FolderScanner.Scan(folder.Path);

                Assert.Equal(new[] { dlss }, result.DlssFiles.Select(file => file.Path));
                Assert.Equal(new[] { dlssd }, result.DlssdFiles.Select(file => file.Path));
                Assert.Equal(new[] { dlssg }, result.DlssgFiles.Select(file => file.Path));
                Assert.Empty(result.BackupFiles);
                Assert.Empty(result.WarningPaths);
            }
        }

        [Fact]
        public void Walk_DoesNotFollowJunctions()
        {
            using (TempFolder folder = new TempFolder())
            {
                string dlss = folder.WriteFile(@"game\nvngx_dlss.dll", "x");

                // A junction back up to the root would otherwise be walked
                // again and again, listing the same DLL under ever longer paths.
                CreateJunction(folder.Combine("game", "loop"), folder.Path);
                // A junction to the same folder would list the DLL twice.
                CreateJunction(folder.Combine("alias"), folder.Combine("game"));

                var results = FolderScanner.FindFiles(folder.Path, "nvngx_dlss.dll");

                Assert.Equal(new[] { dlss }, results.Select(file => file.Path));
            }
        }

        [Fact]
        public void Walk_FollowsTheChosenFolderEvenWhenItIsAJunction()
        {
            using (TempFolder folder = new TempFolder())
            {
                folder.WriteFile(@"real\nvngx_dlss.dll", "x");
                string link = folder.Combine("link");
                CreateJunction(link, folder.Combine("real"));

                var results = FolderScanner.FindFiles(link, "nvngx_dlss.dll");

                Assert.Equal(new[] { Path.Combine(link, "nvngx_dlss.dll") }, results.Select(file => file.Path));
            }
        }

        private static void CreateJunction(string link, string target)
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }))
            {
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }
        }
    }
}
