using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using OnePieceCardScanner.Recognition.Segmentation;
using OpenCvSharp;

namespace OnePieceCardScanner.Services;

public sealed class CharacterSegmentationDebugResult
{
    public string OutputFolder { get; init; } =
        string.Empty;

    public int SegmentCount { get; init; }

    public string ExpectedText { get; init; } =
        string.Empty;

    public bool CountMatches { get; init; }
}

public sealed class CharacterSegmentationDebugService
{
    private readonly CharacterSegmenter _segmenter =
        new();

    public CharacterSegmentationDebugResult Run(
        string imagePath,
        string expectedText)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException(
                "Es wurde kein Bildpfad übergeben.",
                nameof(imagePath));
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException(
                "Das Bild wurde nicht gefunden.",
                imagePath);
        }

        expectedText ??=
            string.Empty;

        string outputFolder =
            Path.Combine(
                AppContext.BaseDirectory,
                "CharacterSegmentationDebug",
                DateTime.Now.ToString(
                    "yyyy-MM-dd_HH-mm-ss-fff"));

        Directory.CreateDirectory(
            outputFolder);

        string sourceCopy =
            Path.Combine(
                outputFolder,
                "00_source.png");

        File.Copy(
            imagePath,
            sourceCopy,
            overwrite: true);

        IReadOnlyList<CharacterSegment> segments =
            _segmenter.Segment(
                imagePath);

        try
        {
            SaveSegments(
                segments,
                expectedText,
                outputFolder);

            SaveReport(
                segments,
                expectedText,
                outputFolder);
        }
        finally
        {
            foreach (CharacterSegment segment in segments)
            {
                segment.Image.Dispose();
            }
        }

        OpenFolder(
            outputFolder);

        return new CharacterSegmentationDebugResult
        {
            OutputFolder =
                outputFolder,

            SegmentCount =
                segments.Count,

            ExpectedText =
                expectedText,

            CountMatches =
                segments.Count ==
                expectedText.Length
        };
    }

    private static void SaveSegments(
        IReadOnlyList<CharacterSegment> segments,
        string expectedText,
        string outputFolder)
    {
        for (int index = 0;
             index < segments.Count;
             index++)
        {
            CharacterSegment segment =
                segments[index];

            string expectedCharacter =
                index < expectedText.Length
                    ? GetSafeCharacterName(
                        expectedText[index])
                    : "UNKNOWN";

            string fileName =
                $"segment_{index:00}_" +
                $"{expectedCharacter}.png";

            string outputPath =
                Path.Combine(
                    outputFolder,
                    fileName);

            Cv2.ImWrite(
                outputPath,
                segment.Image);
        }
    }

    private static void SaveReport(
        IReadOnlyList<CharacterSegment> segments,
        string expectedText,
        string outputFolder)
    {
        var report =
            new StringBuilder();

        report.AppendLine(
            $"Erwarteter Text: {expectedText}");

        report.AppendLine(
            $"Erwartete Zeichen: {expectedText.Length}");

        report.AppendLine(
            $"Gefundene Segmente: {segments.Count}");

        report.AppendLine(
            $"Anzahl stimmt: " +
            $"{segments.Count == expectedText.Length}");

        report.AppendLine();

        for (int index = 0;
             index < segments.Count;
             index++)
        {
            CharacterSegment segment =
                segments[index];

            string expectedCharacter =
                index < expectedText.Length
                    ? expectedText[index].ToString()
                    : "UNBEKANNT";

            report.AppendLine(
                $"Segment {index}:");

            report.AppendLine(
                $"  Erwartet: {expectedCharacter}");

            report.AppendLine(
                $"  Position: " +
                $"X={segment.Bounds.X}, " +
                $"Y={segment.Bounds.Y}");

            report.AppendLine(
                $"  Größe: " +
                $"{segment.Bounds.Width} x " +
                $"{segment.Bounds.Height}");

            report.AppendLine();
        }

        File.WriteAllText(
            Path.Combine(
                outputFolder,
                "result.txt"),
            report.ToString());
    }

    private static string GetSafeCharacterName(
        char character)
    {
        return character switch
        {
            '-' => "DASH",
            '/' => "SLASH",
            '\\' => "BACKSLASH",
            _ => character.ToString()
        };
    }

    private static void OpenFolder(
        string folder)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        folder,

                    UseShellExecute =
                        true
                });
        }
        catch
        {
            // Der Debugordner wurde trotzdem erstellt.
        }
    }
}