using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Пост-экспортный контроль содержимого .unitypackage. Пакет — это gzip-обёрнутый
    /// tar, где каждый ассет лежит в папке "&lt;guid&gt;/" с файлом "pathname",
    /// содержащим путь ассета в проекте. Верификатор читает все pathname-записи и
    /// сообщает о путях, содержащих запрещённые фрагменты (тестовый/внутренний контент).
    /// Введён после инцидента 2026-08-20 (IAPTestPanel попала в деплой).
    /// </summary>
    internal static class SdkPackageVerifier
    {
        /// <summary>
        /// true — пакет чист; false — найдены запрещённые пути (список в error)
        /// либо пакет не удалось прочитать. Сравнение фрагментов без учёта регистра.
        /// </summary>
        public static bool VerifyNoForbiddenPaths(
            string unitypackagePath,
            IReadOnlyList<string> forbiddenPathFragments,
            out string error)
        {
            error = null;

            List<string> assetPaths;
            try
            {
                assetPaths = ReadAssetPaths(unitypackagePath);
            }
            catch (Exception e)
            {
                error = $"Failed to read package for verification: {e.Message}";
                return false;
            }

            var leaked = new List<string>();
            foreach (var path in assetPaths)
            {
                foreach (var fragment in forbiddenPathFragments)
                {
                    if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        leaked.Add(path);
                        break;
                    }
                }
            }

            if (leaked.Count > 0)
            {
                error = "Package contains forbidden content:\n  " + string.Join("\n  ", leaked);
                return false;
            }

            return true;
        }

        private static List<string> ReadAssetPaths(string packagePath)
        {
            var result = new List<string>();
            var header = new byte[512];

            using (var file = File.OpenRead(packagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                while (ReadBlock(gzip, header))
                {
                    if (IsZeroBlock(header))
                        continue;

                    string name = ReadNullTerminatedString(header, 0, 100);
                    long size = ReadOctal(header, 124, 12);
                    long padded = (size + 511L) & ~511L;

                    // pathname-файлы короткие; всё остальное (asset-данные, превью) пропускаем
                    if (name.EndsWith("/pathname", StringComparison.Ordinal) && size > 0 && size < 4096)
                    {
                        var data = new byte[padded];
                        ReadExact(gzip, data, (int)padded);
                        string content = Encoding.UTF8.GetString(data, 0, (int)size);
                        int newline = content.IndexOf('\n');
                        result.Add(newline >= 0 ? content.Substring(0, newline) : content);
                    }
                    else
                    {
                        SkipBytes(gzip, padded);
                    }
                }
            }

            return result;
        }

        /// <summary>false — чистый EOF перед началом блока (конец архива).</summary>
        private static bool ReadBlock(Stream stream, byte[] block)
        {
            int total = 0;
            while (total < block.Length)
            {
                int read = stream.Read(block, total, block.Length - total);
                if (read == 0)
                {
                    if (total == 0)
                        return false;
                    throw new IOException("Unexpected EOF inside tar header.");
                }
                total += read;
            }
            return true;
        }

        private static void ReadExact(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read == 0)
                    throw new IOException("Unexpected EOF inside tar entry data.");
                total += read;
            }
        }

        private static void SkipBytes(Stream stream, long count)
        {
            // GZipStream не поддерживает Seek — пропускаем чтением
            var buffer = new byte[8192];
            long remaining = count;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = stream.Read(buffer, 0, toRead);
                if (read == 0)
                    throw new IOException("Unexpected EOF while skipping tar entry data.");
                remaining -= read;
            }
        }

        private static bool IsZeroBlock(byte[] block)
        {
            for (int i = 0; i < block.Length; i++)
                if (block[i] != 0)
                    return false;
            return true;
        }

        private static string ReadNullTerminatedString(byte[] block, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && block[end] != 0)
                end++;
            return Encoding.ASCII.GetString(block, offset, end - offset);
        }

        private static long ReadOctal(byte[] block, int offset, int length)
        {
            long value = 0;
            for (int i = offset; i < offset + length; i++)
            {
                byte b = block[i];
                if (b == 0 || b == (byte)' ')
                    continue;
                if (b < (byte)'0' || b > (byte)'7')
                    break;
                value = value * 8 + (b - (byte)'0');
            }
            return value;
        }
    }
}
