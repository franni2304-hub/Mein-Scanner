using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;

namespace OnePieceCardScanner.Recognition;

public static class CardRegionExtractor
{
    public static IReadOnlyList<string> ExtractCardNumberCandidates(
        string imagePath)
    {
        using Mat image = Cv2.ImRead(
            imagePath,
            ImreadModes.Color);

        if (image.Empty())
        {
            throw new InvalidOperationException(
                "Die ausgeschnittene Karte konnte nicht geladen werden.");
        }

        var results = new List<string>();

        CandidateDefinition[] candidates =
        {
            new(
                X: 0.56,
                Y: 0.875,
                Width: 0.39,
                Height: 0.105),

            new(
                X: 0.60,
                Y: 0.895,
                Width: 0.35,
                Height: 0.085),

            new(
                X: 0.64,
                Y: 0.915,
                Width: 0.31,
                Height: 0.065),

            new(
                X: 0.60,
                Y: 0.855,
                Width: 0.35,
                Height: 0.085),

            new(
                X: 0.67,
                Y: 0.885,
                Width: 0.28,
                Height: 0.080)
        };

        for (int index = 0;
             index < candidates.Length;
             index++)
        {
            CandidateDefinition candidate =
                candidates[index];

            int x =
                (int)(image.Width * candidate.X);

            int y =
                (int)(image.Height * candidate.Y);

            int width =
                (int)(image.Width * candidate.Width);

            int height =
                (int)(image.Height * candidate.Height);

            Rect region =
                CreateSafeRegion(
                    image,
                    x,
                    y,
                    width,
                    height);

            using Mat cropped =
                new Mat(
                    image,
                    region);

            string outputPath =
                Path.Combine(
                    Path.GetTempPath(),
                    $"onepiece-card-number-{index}.png");

            Cv2.ImWrite(
                outputPath,
                cropped);

            results.Add(
                outputPath);
        }

        return results;
    }

    public static string ExtractRarityRegion(
        string imagePath)
    {
        using Mat image = Cv2.ImRead(
            imagePath,
            ImreadModes.Color);

        if (image.Empty())
        {
            throw new InvalidOperationException(
                "Die Karte konnte nicht geladen werden.");
        }

        int x =
            (int)(image.Width * 0.84);

        int y =
            (int)(image.Height * 0.90);

        int width =
            (int)(image.Width * 0.10);

        int height =
            (int)(image.Height * 0.075);

        Rect region =
            CreateSafeRegion(
                image,
                x,
                y,
                width,
                height);

        using Mat cropped =
            new Mat(
                image,
                region);

        string outputPath =
            Path.Combine(
                Path.GetTempPath(),
                "onepiece-rarity-region.png");

        Cv2.ImWrite(
            outputPath,
            cropped);

        return outputPath;
    }

    private static Rect CreateSafeRegion(
        Mat image,
        int x,
        int y,
        int width,
        int height)
    {
        int safeX =
            Math.Clamp(
                x,
                0,
                image.Width - 1);

        int safeY =
            Math.Clamp(
                y,
                0,
                image.Height - 1);

        int safeWidth =
            Math.Clamp(
                width,
                1,
                image.Width - safeX);

        int safeHeight =
            Math.Clamp(
                height,
                1,
                image.Height - safeY);

        return new Rect(
            safeX,
            safeY,
            safeWidth,
            safeHeight);
    }

    private readonly record struct CandidateDefinition(
        double X,
        double Y,
        double Width,
        double Height);
}