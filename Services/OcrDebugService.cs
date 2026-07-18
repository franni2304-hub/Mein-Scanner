using System;
using System.Diagnostics;
using System.IO;

namespace OnePieceCardScanner.Services;

public static class OcrDebugService
{
    private static string? _sessionFolder;

    public static string? CurrentSessionFolder =>
        _sessionFolder;

    public static string CreateSession()
    {
        string debugRoot = Path.Combine(
            AppContext.BaseDirectory,
            "Debug");

        Directory.CreateDirectory(
            debugRoot);

        _sessionFolder = Path.Combine(
            debugRoot,
            DateTime.Now.ToString(
                "yyyy-MM-dd_HH-mm-ss"));

        Directory.CreateDirectory(
            _sessionFolder);

        return _sessionFolder;
    }

    public static void SaveFile(
        string sourceFile,
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(
                _sessionFolder))
        {
            return;
        }

        if (!File.Exists(sourceFile))
        {
            return;
        }

        string destination = Path.Combine(
            _sessionFolder,
            fileName);

        File.Copy(
            sourceFile,
            destination,
            overwrite: true);
    }

    public static void SaveText(
        string fileName,
        string text)
    {
        if (string.IsNullOrWhiteSpace(
                _sessionFolder))
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(
                _sessionFolder,
                fileName),
            text ?? string.Empty);
    }

    public static void OpenCurrentSessionFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    _sessionFolder))
            {
                return;
            }

            if (!Directory.Exists(
                    _sessionFolder))
            {
                return;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        _sessionFolder,

                    UseShellExecute =
                        true
                });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Debug-Ordner konnte nicht geöffnet werden:\n" +
                $"{exception}");
        }
    }
}
