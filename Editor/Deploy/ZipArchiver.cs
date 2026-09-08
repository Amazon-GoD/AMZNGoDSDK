using System;
using System.IO;
using System.IO.Compression;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Тонкая обёртка над System.IO.Compression.ZipFile. Всё, что нам нужно —
    /// упаковать одну готовую папку в один .zip. Без внешних зависимостей.
    /// Перенесено из Assets/GoDSDKDeploy (namespace сменён на структуру SDK).
    /// </summary>
    internal static class ZipArchiver
    {
        /// <summary>
        /// Пакует содержимое sourceDir в zipPath. Если zipPath уже существует —
        /// перетирает. Родительскую папку под zip создаёт при необходимости.
        /// </summary>
        public static bool CreateFromDirectory(string sourceDir, string zipPath, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                error = $"Source directory does not exist: {sourceDir}";
                return false;
            }

            if (string.IsNullOrEmpty(zipPath))
            {
                error = "Zip path is empty.";
                return false;
            }

            try
            {
                string zipParent = Path.GetDirectoryName(zipPath);
                if (!string.IsNullOrEmpty(zipParent))
                    Directory.CreateDirectory(zipParent);

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ZipFile.CreateFromDirectory(
                    sourceDirectoryName: sourceDir,
                    destinationArchiveFileName: zipPath,
                    compressionLevel: CompressionLevel.Optimal,
                    includeBaseDirectory: false);

                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
