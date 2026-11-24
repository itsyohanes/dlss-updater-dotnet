using System;
using System.IO;
using System.IO.Compression;

namespace DlssUpdater.Tests
{
    // A throwaway folder under %TEMP% that is deleted when the test ends.
    internal sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dlss-updater-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts)
        {
            string result = Path;
            foreach (string part in parts)
            {
                result = System.IO.Path.Combine(result, part);
            }

            return result;
        }

        public string WriteFile(string relativePath, string content)
        {
            string fullPath = Combine(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        public string WriteZip(string relativePath, string entryName, string content)
        {
            string fullPath = Combine(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));

            using (FileStream stream = File.Create(fullPath))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            using (StreamWriter writer = new StreamWriter(archive.CreateEntry(entryName).Open()))
            {
                writer.Write(content);
            }

            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
            }
        }
    }
}
