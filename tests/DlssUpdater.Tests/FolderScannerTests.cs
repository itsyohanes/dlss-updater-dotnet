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
    }
}
