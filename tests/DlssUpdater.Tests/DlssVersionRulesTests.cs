using System;
using System.Collections.Generic;
using dlss_updater.Services;
using Xunit;

namespace DlssUpdater.Tests
{
    public class DlssVersionRulesTests
    {
        [Theory]
        [InlineData("nvngx_dlss.dll", "310.9.0.0", "310.9.16.0", nameof(DlssFileStatus.UpdateAvailable))]
        [InlineData("nvngx_dlss.dll", "310.9.16.0", "310.9.16.0", nameof(DlssFileStatus.UpToDate))]
        [InlineData("nvngx_dlss.dll", "310.10.0.0", "310.9.16.0", nameof(DlssFileStatus.UpToDate))]
        // DLSS 2 and DLSS 3 builds can move to each other.
        [InlineData("nvngx_dlss.dll", "2.5.1.0", "310.9.16.0", nameof(DlssFileStatus.UpdateAvailable))]
        [InlineData("nvngx_dlssd.dll", "3.7.10.0", "310.9.16.0", nameof(DlssFileStatus.UpdateAvailable))]
        // A new major version is not treated as a drop-in replacement.
        [InlineData("nvngx_dlss.dll", "310.9.16.0", "410.1.0.0", nameof(DlssFileStatus.Incompatible))]
        [InlineData("nvngx_dlssd.dll", "1.0.0.0", "310.9.16.0", nameof(DlssFileStatus.Incompatible))]
        // Frame generation 1.x counts as DLSS 3.
        [InlineData("nvngx_dlssg.dll", "1.0.7.0", "310.9.16.0", nameof(DlssFileStatus.UpdateAvailable))]
        [InlineData("NVNGX_DLSSG.DLL", "1.0.7.0", "310.9.16.0", nameof(DlssFileStatus.UpdateAvailable))]
        public void Evaluate_ComparesInstalledAgainstManifest(
            string fileName,
            string installed,
            string manifest,
            string expected)
        {
            Version target;

            DlssFileStatus status = DlssVersionRules.Evaluate(
                fileName,
                Version.Parse(installed),
                manifest,
                out target);

            Assert.Equal((DlssFileStatus)Enum.Parse(typeof(DlssFileStatus), expected), status);
            Assert.Equal(Version.Parse(manifest), target);
        }

        [Theory]
        [InlineData("1.0.0.0", nameof(DlssFileStatus.UpdateAvailable))]
        [InlineData("1.3.2.0", nameof(DlssFileStatus.UpToDate))]
        public void Evaluate_Dlss1MovesToPinnedLegacyBuildInsteadOfManifest(
            string installed,
            string expected)
        {
            Version target;

            DlssFileStatus status = DlssVersionRules.Evaluate(
                "nvngx_dlss.dll",
                Version.Parse(installed),
                "310.9.16.0",
                out target);

            Assert.Equal((DlssFileStatus)Enum.Parse(typeof(DlssFileStatus), expected), status);
            Assert.Equal(new Version(1, 3, 2, 0), target);
        }

        [Fact]
        public void Evaluate_NoInstalledVersionIsUnknown()
        {
            Version target;

            DlssFileStatus status = DlssVersionRules.Evaluate(
                "nvngx_dlss.dll",
                (Version)null,
                "310.9.16.0",
                out target);

            Assert.Equal(DlssFileStatus.Unknown, status);
            Assert.Null(target);
        }

        [Fact]
        public void Evaluate_UnreadableManifestVersionIsUnknown()
        {
            Version target;

            DlssFileStatus status = DlssVersionRules.Evaluate(
                "nvngx_dlss.dll",
                new Version(310, 9, 0, 0),
                "not-a-version",
                out target);

            Assert.Equal(DlssFileStatus.Unknown, status);
            Assert.Null(target);
        }

        [Fact]
        public void Evaluate_PathOverloadLooksUpTheFileName()
        {
            Dictionary<string, string> latest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["nvngx_dlss.dll"] = "310.9.16.0"
            };
            Version target;

            DlssFileStatus status = DlssVersionRules.Evaluate(
                @"C:\Apps\Example\bin\nvngx_dlss.dll",
                "310.9.0.0",
                latest,
                out target);

            Assert.Equal(DlssFileStatus.UpdateAvailable, status);
            Assert.Equal(new Version(310, 9, 16, 0), target);
        }

        [Theory]
        [InlineData(@"C:\Apps\Example\nvngx_dlssg.dll", "310.9.0.0")]
        [InlineData(@"C:\Apps\Example\nvngx_dlss.dll", "Unknown")]
        [InlineData(null, "310.9.0.0")]
        public void Evaluate_PathOverloadReturnsUnknownWhenItCannotCompare(
            string path,
            string installed)
        {
            Dictionary<string, string> latest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["nvngx_dlss.dll"] = "310.9.16.0"
            };
            Version target;

            Assert.Equal(
                DlssFileStatus.Unknown,
                DlssVersionRules.Evaluate(path, installed, latest, out target));
            Assert.Equal(
                DlssFileStatus.Unknown,
                DlssVersionRules.Evaluate(path, installed, (IDictionary<string, string>)null, out target));
        }
    }
}
