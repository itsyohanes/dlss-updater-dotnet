using System;
using System.Collections.Generic;
using System.IO;

namespace dlss_updater.Services
{
    internal enum DlssFileStatus
    {
        Unknown,
        UpToDate,
        UpdateAvailable,
        Incompatible
    }

    // The one place that decides whether an installed DLL can move to the
    // manifest's version. The update itself, the after-scan check and the
    // saved games list all go through here so they always agree.
    internal static class DlssVersionRules
    {
        public static DlssFileStatus Evaluate(
            string fileName,
            Version installedVersion,
            string manifestVersion,
            out Version targetVersion)
        {
            targetVersion = null;

            if (installedVersion == null)
            {
                return DlssFileStatus.Unknown;
            }

            if (IsLegacyDlss(fileName, installedVersion))
            {
                manifestVersion = DlssReleaseClient.GetLegacyDlssAsset().Version;
            }

            if (!Version.TryParse(manifestVersion, out targetVersion))
            {
                targetVersion = null;
                return DlssFileStatus.Unknown;
            }

            if (targetVersion <= installedVersion)
            {
                return DlssFileStatus.UpToDate;
            }

            return AreVersionsCompatible(fileName, installedVersion, targetVersion)
                ? DlssFileStatus.UpdateAvailable
                : DlssFileStatus.Incompatible;
        }

        // Convenience overload for callers holding a file path, a version
        // string and the name -> version map from the manifest.
        public static DlssFileStatus Evaluate(
            string filePath,
            string installedVersion,
            IDictionary<string, string> latestVersions,
            out Version targetVersion)
        {
            targetVersion = null;
            Version installed;
            string manifestVersion;
            string fileName = Path.GetFileName(filePath ?? string.Empty);

            if (latestVersions == null ||
                !Version.TryParse(installedVersion, out installed) ||
                !latestVersions.TryGetValue(fileName, out manifestVersion))
            {
                return DlssFileStatus.Unknown;
            }

            return Evaluate(fileName, installed, manifestVersion, out targetVersion);
        }

        public static bool IsLegacyDlss(string fileName, Version installedVersion)
        {
            return string.Equals(
                    fileName,
                    "nvngx_dlss.dll",
                    StringComparison.OrdinalIgnoreCase) &&
                GetDlssCompatibilityMajor(installedVersion.Major) == 1;
        }

        private static bool AreVersionsCompatible(
            string fileName,
            Version installedVersion,
            Version latestVersion)
        {
            int installedMajor = GetDlssCompatibilityMajor(installedVersion.Major);
            int latestMajor = GetDlssCompatibilityMajor(latestVersion.Major);

            if (string.Equals(
                fileName,
                "nvngx_dlssg.dll",
                StringComparison.OrdinalIgnoreCase))
            {
                installedMajor = NormalizeFrameGenerationMajor(installedMajor);
                latestMajor = NormalizeFrameGenerationMajor(latestMajor);
            }

            if (installedMajor == latestMajor)
            {
                return true;
            }

            return (installedMajor == 2 || installedMajor == 3) &&
                (latestMajor == 2 || latestMajor == 3);
        }

        private static int NormalizeFrameGenerationMajor(int major)
        {
            return major == 1 ? 3 : major;
        }

        private static int GetDlssCompatibilityMajor(int fileVersionMajor)
        {
            return fileVersionMajor >= 100
                ? fileVersionMajor / 100
                : fileVersionMajor;
        }
    }
}
