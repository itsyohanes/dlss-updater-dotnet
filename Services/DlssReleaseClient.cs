using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace dlss_updater.Services
{
    [DataContract]
    internal sealed class DlssReleaseAsset
    {
        [DataMember(Name = "version", IsRequired = true)]
        public string Version { get; set; }

        [DataMember(Name = "url", IsRequired = true)]
        public string Url { get; set; }

        [DataMember(Name = "sha256", EmitDefaultValue = false)]
        public string Sha256 { get; set; }

        // Optional compressed copy of the DLL. "sha256" stays the hash of the
        // extracted DLL; "zipSha256" is the hash of the zip itself. Older app
        // versions ignore these fields and keep downloading "url".
        [DataMember(Name = "zipUrl", EmitDefaultValue = false)]
        public string ZipUrl { get; set; }

        [DataMember(Name = "zipSha256", EmitDefaultValue = false)]
        public string ZipSha256 { get; set; }
    }

    [DataContract]
    internal sealed class GitHubRelease
    {
        [DataMember(Name = "assets")]
        public List<GitHubReleaseAsset> Assets { get; set; }
    }

    [DataContract]
    internal sealed class GitHubReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "digest")]
        public string Digest { get; set; }
    }

    internal static class DlssReleaseClient
    {
        // DLSS files live in a fixed-tag release that is never marked Latest,
        // so app releases in the same repo can be Latest without affecting it.
        private const string FilesReleaseTag = "dlss-files";
        private const string FilesDownloadPath =
            "/itsyohanes/dlss-updater-dotnet/releases/download/" + FilesReleaseTag + "/";
        private const string ManifestUrl =
            "https://github.com" + FilesDownloadPath + "dlss_manifest.json";
        private const string FilesReleaseApiUrl =
            "https://api.github.com/repos/itsyohanes/dlss-updater-dotnet/releases/tags/" + FilesReleaseTag;
        private const string LegacyDlssFileName = "nvngx_dlss.dll";
        private const string LegacyDlssVersion = "1.3.2.0";
        private const string LegacyDlssUrl =
            "https://github.com" + FilesDownloadPath + "nvngx_dlss-1.3.2.dll";
        private const string LegacyDlssSha256 =
            "53271884b0f3e89af0de27f6373d727101e61a711f3b6e3f9d0a775b32d278c0";

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("DLSSUpdater", "1.0"));

            return client;
        }

        // How long a version-only manifest fetch is reused, so scanning several
        // folders in a row doesn't download the manifest every time.
        private static readonly TimeSpan LatestVersionsCacheDuration =
            TimeSpan.FromMinutes(5);
        private static readonly object LatestVersionsLock = new object();
        private static Task<Dictionary<string, string>> _latestVersionsTask;
        private static DateTime _latestVersionsFetchedUtc;

        // Version-only view of the manifest: file name -> version. Unlike
        // GetLatestManifestAsync it never calls the GitHub API for digests,
        // so it's cheap enough to run after every scan and on startup.
        public static Task<Dictionary<string, string>> GetLatestVersionsAsync()
        {
            lock (LatestVersionsLock)
            {
                Task<Dictionary<string, string>> cached = _latestVersionsTask;
                if (cached != null &&
                    !cached.IsFaulted &&
                    !cached.IsCanceled &&
                    DateTime.UtcNow - _latestVersionsFetchedUtc < LatestVersionsCacheDuration)
                {
                    return cached;
                }

                _latestVersionsFetchedUtc = DateTime.UtcNow;
                _latestVersionsTask = FetchLatestVersionsAsync();
                return _latestVersionsTask;
            }
        }

        private static async Task<Dictionary<string, string>> FetchLatestVersionsAsync()
        {
            Dictionary<string, DlssReleaseAsset> downloadedManifest =
                await DownloadManifestAsync().ConfigureAwait(false);

            return ToVersionMap(downloadedManifest);
        }

        private static Dictionary<string, string> ToVersionMap(
            IDictionary<string, DlssReleaseAsset> manifest)
        {
            Dictionary<string, string> versions =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, DlssReleaseAsset> item in manifest)
            {
                Version parsed;
                if (IsSupportedDll(item.Key) &&
                    item.Value != null &&
                    Version.TryParse(item.Value.Version, out parsed))
                {
                    versions[item.Key] = item.Value.Version;
                }
            }

            if (versions.Count == 0)
            {
                throw new InvalidDataException(Translator.T("EmptyManifest"));
            }

            return versions;
        }

        // The full manifest was just downloaded for an update: reuse its
        // versions for the update check so both see the same manifest.
        private static void RememberLatestVersions(
            IDictionary<string, DlssReleaseAsset> manifest)
        {
            Dictionary<string, string> versions;

            try
            {
                versions = ToVersionMap(manifest);
            }
            catch (InvalidDataException)
            {
                return;
            }

            lock (LatestVersionsLock)
            {
                _latestVersionsFetchedUtc = DateTime.UtcNow;
                _latestVersionsTask = Task.FromResult(versions);
            }
        }

        private static async Task<Dictionary<string, DlssReleaseAsset>> DownloadManifestAsync()
        {
            string json =
                await HttpClient.GetStringAsync(ManifestUrl).ConfigureAwait(false);

            DataContractJsonSerializerSettings serializerSettings =
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                };

            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(
                    typeof(Dictionary<string, DlssReleaseAsset>),
                    serializerSettings);

            using (MemoryStream stream =
                new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (Dictionary<string, DlssReleaseAsset>)
                    serializer.ReadObject(stream);
            }
        }

        public static async Task<Dictionary<string, DlssReleaseAsset>>
            GetLatestManifestAsync()
        {
            Dictionary<string, DlssReleaseAsset> downloadedManifest =
                await DownloadManifestAsync();

            Dictionary<string, DlssReleaseAsset> manifest =
                new Dictionary<string, DlssReleaseAsset>(
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> releaseDigests = null;

            foreach (KeyValuePair<string, DlssReleaseAsset> item
                in downloadedManifest)
            {
                if (!IsSupportedDll(item.Key) ||
                    item.Value == null ||
                    string.IsNullOrWhiteSpace(item.Value.Version) ||
                    !IsTrustedDownloadUrl(item.Value.Url))
                {
                    continue;
                }

                if (!IsSha256(item.Value.Sha256))
                {
                    if (releaseDigests == null)
                    {
                        releaseDigests = await GetFilesReleaseAssetSha256Async();
                    }

                    string releaseDigest;
                    if (releaseDigests.TryGetValue(item.Key, out releaseDigest))
                    {
                        item.Value.Sha256 = releaseDigest;
                    }
                }

                if (!IsSha256(item.Value.Sha256))
                {
                    throw new InvalidDataException(
                        Translator.T("MissingDigest", item.Key));
                }

                item.Value.Sha256 = item.Value.Sha256.ToLowerInvariant();

                if (!string.IsNullOrWhiteSpace(item.Value.ZipUrl))
                {
                    if (IsTrustedDownloadUrl(item.Value.ZipUrl) &&
                        !IsSha256(item.Value.ZipSha256))
                    {
                        if (releaseDigests == null)
                        {
                            releaseDigests = await GetFilesReleaseAssetSha256Async();
                        }

                        string zipDigest;
                        if (releaseDigests.TryGetValue(
                            GetAssetName(item.Value.ZipUrl), out zipDigest))
                        {
                            item.Value.ZipSha256 = zipDigest;
                        }
                    }

                    if (IsTrustedDownloadUrl(item.Value.ZipUrl) &&
                        IsSha256(item.Value.ZipSha256))
                    {
                        item.Value.ZipSha256 = item.Value.ZipSha256.ToLowerInvariant();
                    }
                    else
                    {
                        // Unusable zip entry: fall back to the plain DLL.
                        item.Value.ZipUrl = null;
                        item.Value.ZipSha256 = null;
                    }
                }

                manifest[item.Key] = item.Value;
            }

            if (manifest.Count == 0)
            {
                throw new InvalidDataException(
                    Translator.T("EmptyManifest"));
            }

            RememberLatestVersions(manifest);
            return manifest;
        }

        public static DlssReleaseAsset GetLegacyDlssAsset()
        {
            return new DlssReleaseAsset
            {
                Version = LegacyDlssVersion,
                Url = LegacyDlssUrl,
                Sha256 = LegacyDlssSha256
            };
        }

        public static async Task<string> DownloadToTemporaryFileAsync(
            string url,
            string fileName)
        {
            string temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "dlss-updater-" + Guid.NewGuid().ToString("N") + "-" + fileName);

            CancellationTokenSource stall = new CancellationTokenSource();

            try
            {
                using (HttpResponseMessage response =
                    await HttpClient.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? announced = response.Content.Headers.ContentLength;
                    if (announced > MaxDownloadBytes)
                    {
                        throw new InvalidDataException(Translator.T("DownloadTooLarge", fileName));
                    }

                    // HttpClient.Timeout stops applying once the headers
                    // arrive, so a connection that goes quiet mid-download
                    // would hang. Closing the response ends the pending read.
                    using (stall.Token.Register(response.Dispose))
                    using (Stream source = await response.Content.ReadAsStreamAsync())
                    using (FileStream destination = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long total = 0;

                        while (true)
                        {
                            stall.CancelAfter(DownloadStallTimeout);
                            int read = await source.ReadAsync(buffer, 0, buffer.Length);
                            if (read <= 0)
                            {
                                break;
                            }

                            total += read;
                            if (total > MaxDownloadBytes)
                            {
                                throw new InvalidDataException(Translator.T("DownloadTooLarge", fileName));
                            }

                            await destination.WriteAsync(buffer, 0, read);
                        }

                        stall.CancelAfter(Timeout.Infinite);
                    }
                }

                return temporaryPath;
            }
            catch (Exception) when (stall.IsCancellationRequested)
            {
                TryDeleteFile(temporaryPath);
                throw new TimeoutException(Translator.T("DownloadStalled", fileName));
            }
            catch
            {
                TryDeleteFile(temporaryPath);
                throw;
            }
            finally
            {
                stall.Dispose();
            }
        }

        public static bool TryGetCachedFile(
            string fileName,
            string version,
            string expectedSha256,
            out string cachedPath)
        {
            cachedPath = GetCachedFilePath(fileName, version);

            try
            {
                if (!File.Exists(cachedPath) || new FileInfo(cachedPath).Length == 0)
                {
                    return false;
                }

                if (FileMatchesSha256(cachedPath, expectedSha256) &&
                    HasRequiredSignature(cachedPath, expectedSha256))
                {
                    return true;
                }

                TryDeleteFile(cachedPath);
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> DownloadToCacheAsync(
            DlssReleaseAsset asset,
            string fileName,
            string version)
        {
            if (!string.IsNullOrWhiteSpace(asset.ZipUrl) &&
                IsSha256(asset.ZipSha256))
            {
                try
                {
                    return await DownloadToCacheAsync(
                        () => DownloadZippedDllAsync(asset, fileName),
                        fileName,
                        version,
                        asset.Sha256);
                }
                catch
                {
                    // Fall back to the uncompressed DLL below.
                }
            }

            return await DownloadToCacheAsync(
                () => DownloadToTemporaryFileAsync(asset.Url, fileName),
                fileName,
                version,
                asset.Sha256);
        }

        private const long MaxExtractedDllBytes = 512L * 1024 * 1024;
        private const long MaxDownloadBytes = 512L * 1024 * 1024;
        private static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(30);

        private static async Task<string> DownloadZippedDllAsync(
            DlssReleaseAsset asset,
            string fileName)
        {
            string zipPath = null;
            string extractedPath = Path.Combine(
                Path.GetTempPath(),
                "dlss-updater-" + Guid.NewGuid().ToString("N") + "-" + fileName);

            try
            {
                zipPath = await DownloadToTemporaryFileAsync(
                    asset.ZipUrl,
                    GetAssetName(asset.ZipUrl));

                if (!FileMatchesSha256(zipPath, asset.ZipSha256))
                {
                    throw new InvalidDataException(
                        Translator.T("ShaMismatch", GetAssetName(asset.ZipUrl)));
                }

                using (FileStream zipStream = File.OpenRead(zipPath))
                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry match = null;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        // Only a top-level entry named exactly like the DLL.
                        if (string.Equals(entry.FullName, fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (match != null)
                            {
                                throw new InvalidDataException(fileName);
                            }

                            match = entry;
                        }
                    }

                    if (match == null || match.Length <= 0 || match.Length > MaxExtractedDllBytes)
                    {
                        throw new InvalidDataException(fileName);
                    }

                    using (Stream source = match.Open())
                    using (FileStream destination = new FileStream(
                        extractedPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long total = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            total += read;
                            if (total > MaxExtractedDllBytes)
                            {
                                throw new InvalidDataException(fileName);
                            }

                            await destination.WriteAsync(buffer, 0, read);
                        }
                    }
                }

                return extractedPath;
            }
            catch
            {
                TryDeleteFile(extractedPath);
                throw;
            }
            finally
            {
                TryDeleteFile(zipPath);
            }
        }

        private static string GetAssetName(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath))
                : string.Empty;
        }

        private static async Task<string> DownloadToCacheAsync(
            Func<Task<string>> download,
            string fileName,
            string version,
            string expectedSha256)
        {
            string cacheDirectory = GetCacheDirectory();
            string cachePath = GetCachedFilePath(fileName, version);

            Directory.CreateDirectory(cacheDirectory);

            string downloadedPath = null;
            string stagingPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                downloadedPath = await download();
                if (!FileMatchesSha256(downloadedPath, expectedSha256))
                {
                    throw new InvalidDataException(
                        Translator.T("ShaMismatch", fileName));
                }

                if (!HasRequiredSignature(downloadedPath, expectedSha256))
                {
                    throw new InvalidDataException(
                        Translator.T("NotSignedByNvidia", fileName));
                }

                File.Copy(downloadedPath, stagingPath, false);

                if (File.Exists(cachePath))
                {
                    File.Replace(stagingPath, cachePath, null);
                }
                else
                {
                    File.Move(stagingPath, cachePath);
                }

                if (!IsLegacyDlss(fileName, version))
                {
                    RemoveOlderCachedVersions(cacheDirectory, fileName, cachePath);
                }

                return cachePath;
            }
            finally
            {
                TryDeleteFile(downloadedPath);
                TryDeleteFile(stagingPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static async Task<Dictionary<string, string>> GetFilesReleaseAssetSha256Async()
        {
            using (HttpResponseMessage response = await HttpClient.GetAsync(FilesReleaseApiUrl))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                GitHubRelease release;
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    release = (GitHubRelease)serializer.ReadObject(stream);
                }

                Dictionary<string, string> digests =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (release?.Assets != null)
                {
                    foreach (GitHubReleaseAsset asset in release.Assets)
                    {
                        if (asset != null &&
                            !string.IsNullOrWhiteSpace(asset.Name) &&
                            asset.Digest != null &&
                            asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        {
                            string digest = asset.Digest.Substring("sha256:".Length);
                            if (IsSha256(digest))
                            {
                                digests[asset.Name] = digest;
                            }
                        }
                    }
                }

                return digests;
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FileMatchesSha256(string path, string expectedSha256)
        {
            if (!IsSha256(expectedSha256))
            {
                return false;
            }

            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    result.Append(value.ToString("x2"));
                }

                return string.Equals(result.ToString(), expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string GetCacheDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DLSS Updater",
                "Cache");
        }

        private static string GetCachedFilePath(
            string fileName,
            string version)
        {
            return Path.Combine(
                GetCacheDirectory(),
                fileName + "." + version + ".cache");
        }

        private static void RemoveOlderCachedVersions(
            string cacheDirectory,
            string fileName,
            string currentCachePath)
        {
            try
            {
                foreach (string path in Directory.GetFiles(
                    cacheDirectory,
                    fileName + ".*.cache"))
                {
                    if (!string.Equals(
                            path,
                            currentCachePath,
                            StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(
                            path,
                            GetCachedFilePath(LegacyDlssFileName, LegacyDlssVersion),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        // Every DLL must be signed by NVIDIA, except the legacy DLSS 1 file:
        // its hash is built into the app, so the release can't change it.
        private static bool HasRequiredSignature(string path, string expectedSha256)
        {
            return string.Equals(expectedSha256, LegacyDlssSha256, StringComparison.OrdinalIgnoreCase) ||
                NvidiaSignature.IsSignedByNvidia(path);
        }

        private static bool IsLegacyDlss(string fileName, string version)
        {
            return string.Equals(fileName, LegacyDlssFileName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(version, LegacyDlssVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedDll(string fileName)
        {
            return string.Equals(fileName, "nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "nvngx_dlssd.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "nvngx_dlssg.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedDownloadUrl(string url)
        {
            Uri uri;

            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith(
                    FilesDownloadPath,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
