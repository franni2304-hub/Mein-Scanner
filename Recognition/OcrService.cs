using System;
using System.IO;
using Tesseract;

namespace OnePieceCardScanner.Recognition;

public static class OcrService
{
    public static string ReadText(
        string imagePath)
    {
        string tessdataPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "tessdata");

        if (!Directory.Exists(tessdataPath))
        {
            throw new DirectoryNotFoundException(
                $"Der tessdata-Ordner wurde nicht gefunden:\n" +
                $"{tessdataPath}");
        }

        using var engine =
            new TesseractEngine(
                tessdataPath,
                "eng",
                EngineMode.Default);

        engine.SetVariable(
            "tessedit_char_whitelist",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-");

        engine.SetVariable(
            "preserve_interword_spaces",
            "1");

        using var image =
            Pix.LoadFromFile(
                imagePath);

        using var page =
            engine.Process(
                image,
                PageSegMode.SparseText);

        return page
            .GetText()
            .ToUpperInvariant()
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    public static string ReadRarity(
        string imagePath)
    {
        string tessdataPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "tessdata");

        if (!Directory.Exists(tessdataPath))
        {
            throw new DirectoryNotFoundException(
                $"Der tessdata-Ordner wurde nicht gefunden:\n" +
                $"{tessdataPath}");
        }

        using var engine =
            new TesseractEngine(
                tessdataPath,
                "eng",
                EngineMode.Default);

        engine.SetVariable(
            "tessedit_char_whitelist",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ");

        engine.SetVariable(
            "load_system_dawg",
            "0");

        engine.SetVariable(
            "load_freq_dawg",
            "0");

        using var image =
            Pix.LoadFromFile(
                imagePath);

        using var page =
            engine.Process(
                image,
                PageSegMode.SingleChar);

        return page
            .GetText()
            .ToUpperInvariant()
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();
    }
}