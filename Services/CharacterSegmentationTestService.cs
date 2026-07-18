using System;
using System.Collections.Generic;
using System.IO;
using OnePieceCardScanner.Recognition.Segmentation;
using OpenCvSharp;

namespace OnePieceCardScanner.Services;

public sealed class CharacterSegmentationTestService
{
    private readonly CharacterSegmenter _segmenter =
        new();

    public string Run(
        string preprocessedImagePath)
    {
        if (string.IsNullOrWhiteSpace(
                preprocessedImagePath))
        {
            throw new ArgumentException(
                "Es wurde kein Bildpfad übergeben.",
                nameof(preprocessedImagePath));
        }

        if (!File.Exists(
                preprocessedImagePath))
        {
            throw new FileNotFoundException(
                "Das vorbereitete Nummernbild wurde nicht gefunden.",
                preprocessedImagePath);
        }

        string outputFolder =
            Path.Combine(
                AppContext.BaseDirectory,
                "CharacterSegmentationTest",
                DateTime.Now.ToString(
                    "yyyy-MM-dd_HH-mm-ss"));

        Directory.CreateDirectory(
            outputFolder);

        IReadOnlyList<CharacterSegment> segments =
            _segmenter.Segment(
                preprocessedImagePath);

        for (int index = 0;
             index < segments.Count;
             index++)
        {
            CharacterSegment segment =
                segments[index];

            string outputPath =
                Path.Combine(
                    outputFolder,
                    $"segment_{index}.png");

            Cv2.ImWrite(
                outputPath,
                segment.Image);
        }

        File.WriteAllText(
            Path.Combine(
                outputFolder,
                "result.txt"),
            $"Gefundene Segmente: {segments.Count}");

        foreach (CharacterSegment segment in segments)
        {
            segment.Image.Dispose();
        }

        return outputFolder;
    }
}