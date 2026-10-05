using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class FileHashUtility
{
    // Размер буфера для чтения файла
    private const int BufferSize = 4096 * 16; // Увеличенный буфер для больших видео

    /// <summary>
    /// Сравнивает хэш существующего файла с данными из DownloadHandler.data
    /// </summary>
    /// <param name="existingFilePath">Путь к существующему файлу</param>
    /// <param name="downloadHandlerData">Данные из downloadHandler.data</param>
    /// <param name="algorithm">Алгоритм хэширования (по умолчанию SHA256)</param>
    /// <returns>True если хэши идентичны, иначе False</returns>
    public static async Task<bool> CompareExistingFileWithDownloadedDataAsync(
        string existingFilePath,
        byte[] downloadHandlerData,
        HashAlgorithm algorithm = null)
    {
        // Быстрая проверка: сравниваем размеры
        FileInfo fileInfo = new FileInfo(existingFilePath);
        if (!fileInfo.Exists)
        {
            Debug.LogError($"Файл не существует: {existingFilePath}");
            return false;
        }

        if (fileInfo.Length != downloadHandlerData.LongLength)
        {
            Debug.Log($"Размеры не совпадают. Файл: {fileInfo.Length} байт, Данные: {downloadHandlerData.LongLength} байт");
            return false;
        }

        // Вычисляем хэши асинхронно параллельно для производительности
        Task<string> existingFileHashTask = CalculateFileHashAsync(existingFilePath, algorithm);
        Task<string> downloadedDataHashTask = CalculateHashFromBytesAsync(downloadHandlerData, algorithm);

        // Ожидаем завершения обоих задач
        string existingFileHash = await existingFileHashTask;
        string downloadedDataHash = await downloadedDataHashTask;

        // Сравниваем хэши
        bool areEqual = string.Equals(existingFileHash, downloadedDataHash, StringComparison.OrdinalIgnoreCase);

        Debug.Log($"Хэш существующего файла: {existingFileHash}");
        Debug.Log($"Хэш скачанных данных: {downloadedDataHash}");
        Debug.Log($"Данные идентичны: {areEqual}");

        return areEqual;
    }

    /// <summary>
    /// Синхронная версия сравнения (может вызывать фризы на больших файлах)
    /// </summary>
    public static bool CompareExistingFileWithDownloadedData(
        string existingFilePath,
        byte[] downloadHandlerData,
        HashAlgorithm algorithm = null)
    {
        // Быстрая проверка размеров
        FileInfo fileInfo = new FileInfo(existingFilePath);
        if (!fileInfo.Exists) return false;
        if (fileInfo.Length != downloadHandlerData.LongLength) return false;

        // Вычисляем хэши
        string existingFileHash = CalculateFileHash(existingFilePath, algorithm);
        string downloadedDataHash = CalculateHashFromBytes(downloadHandlerData, algorithm);

        return string.Equals(existingFileHash, downloadedDataHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Вычисляет хэш-сумму файла по указанному пути асинхронно
    /// </summary>
    private static async Task<string> CalculateFileHashAsync(string filePath, HashAlgorithm algorithm = null)
    {
        using (var hashAlgorithm = algorithm ?? SHA256.Create())
        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        {
            hashAlgorithm.Initialize();
            byte[] buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            hashAlgorithm.TransformFinalBlock(buffer, 0, 0);
            return BytesToHexString(hashAlgorithm.Hash);
        }
    }

    /// <summary>
    /// Вычисляет хэш-сумму из массива байтов асинхронно
    /// </summary>
    private static async Task<string> CalculateHashFromBytesAsync(byte[] data, HashAlgorithm algorithm = null)
    {
        // Для асинхронной обработки байтов используем Task.Run
        return await Task.Run(() =>
        {
            using (var hashAlgorithm = algorithm ?? SHA256.Create())
            {
                byte[] hashBytes = hashAlgorithm.ComputeHash(data);
                return BytesToHexString(hashBytes);
            }
        });
    }

    /// <summary>
    /// Синхронное вычисление хэша файла
    /// </summary>
    private static string CalculateFileHash(string filePath, HashAlgorithm algorithm = null)
    {
        using (var hashAlgorithm = algorithm ?? SHA256.Create())
        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
        {
            byte[] hashBytes = hashAlgorithm.ComputeHash(fileStream);
            return BytesToHexString(hashBytes);
        }
    }

    /// <summary>
    /// Синхронное вычисление хэша из массива байтов
    /// </summary>
    private static string CalculateHashFromBytes(byte[] data, HashAlgorithm algorithm = null)
    {
        using (var hashAlgorithm = algorithm ?? SHA256.Create())
        {
            byte[] hashBytes = hashAlgorithm.ComputeHash(data);
            return BytesToHexString(hashBytes);
        }
    }

    /// <summary>
    /// Конвертирует массив байтов в hex-строку
    /// </summary>
    private static string BytesToHexString(byte[] bytes)
    {
        StringBuilder sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}