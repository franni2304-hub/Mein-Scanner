using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnePieceCardScanner.Recognition.OCR;

/// <summary>
/// Schneidet die Kartennummer anhand ihrer normierten Position
/// auf einer bereits erkannten und entzerrten Karte aus.
/// </summary>
public static class FixedCardNumberRegionExtractor
{
    /*
     * Die Positionen sind relativ zur Kartengröße.
     *
     * Die enge Variante wird zuerst geprüft. Die beiden größeren
     * Varianten gleichen kleine Abweichungen beim Kartenzuschnitt aus.
     */
    private static readonly CropDefinition[] CropDefinitions =
    [
        new CropDefinition(
            Name: "tight",
            X: 0.770,
            Y: 0.952,
            Width: 0.155,
            Height: 0.027),

        new CropDefinition(
            Name: "normal",
            X: 0.745,
            Y: 0.944,
            Width: 0.190,
            Height: 0.038),

        new CropDefinition(
            Name: "wide",
            X: 0.715,
            Y: 0.936,
            Width: 0.225,
            Height: 0.050)
    ];

    public static IReadOnlyList<string> ExtractCandidates(
        string detectedCardPath,
        string outputFolder)
    {
        if (!File.Exists(
                detectedCardPath))
        {
            throw new FileNotFoundException(
                "Das erkannte Kartenbild wurde nicht gefunden.",
                detectedCardPath);
        }

        Directory.CreateDirectory(
            outputFolder);

        using Mat loaded =
            Cv2.ImRead(
                detectedCardPath,
                ImreadModes.Color);

        if (loaded.Empty())
        {
            return [];
        }

        using Mat card =
            EnsurePortraitOrientation(
                loaded);

        string sourceName =
            MakeSafeFileName(
                Path.GetFileNameWithoutExtension(
                    detectedCardPath));

        var outputPaths =
            new List<string>();

        foreach (CropDefinition definition in
                 CropDefinitions)
        {
            Rect bounds =
                CalculateBounds(
                    card.Size(),
                    definition);

            if (bounds.Width < 5 ||
                bounds.Height < 5)
            {
                continue;
            }

            using Mat cropped =
                new Mat(
                    card,
                    bounds);

            string outputPath =
                Path.Combine(
                    outputFolder,
                    $"{sourceName}_number_{definition.Name}.png");

            Cv2.ImWrite(
                outputPath,
                cropped);

            outputPaths.Add(
                outputPath);
        }

        return outputPaths;
    }

    private static Mat EnsurePortraitOrientation(
        Mat source)
    {
        if (source.Height >=
            source.Width)
        {
            return source.Clone();
        }

        var rotated =
            new Mat();

        Cv2.Rotate(
            source,
            rotated,
            RotateFlags.Rotate90Clockwise);

        return rotated;
    }

    private static Rect CalculateBounds(
        Size imageSize,
        CropDefinition definition)
    {
        int x =
            (int)Math.Round(
                imageSize.Width *
                definition.X);

        int y =
            (int)Math.Round(
                imageSize.Height *
                definition.Y);

        int width =
            (int)Math.Round(
                imageSize.Width *
                definition.Width);

        int height =
            (int)Math.Round(
                imageSize.Height *
                definition.Height);

        x =
            Math.Clamp(
                x,
                0,
                Math.Max(
                    0,
                    imageSize.Width - 1));

        y =
            Math.Clamp(
                y,
                0,
                Math.Max(
                    0,
                    imageSize.Height - 1));

        width =
            Math.Clamp(
                width,
                1,
                imageSize.Width - x);

        height =
            Math.Clamp(
                height,
                1,
                imageSize.Height - y);

        return new Rect(
            x,
            y,
            width,
            height);
    }

    private static string MakeSafeFileName(
        string value)
    {
        char[] invalid =
            Path.GetInvalidFileNameChars();

        return new string(
            value
                .Select(character =>
                    invalid.Contains(character)
                        ? '_'
                        : character)
                .ToArray());
    }

    private sealed record CropDefinition(
        string Name,
        double X,
        double Y,
        double Width,
        double Height);
}
