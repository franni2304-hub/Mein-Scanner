using OpenCvSharp;
using System;
using System.IO;

namespace OnePieceCardScanner.Recognition;

public static class CardImagePreprocessor
{
    /// <summary>
    /// Bereitet einen bereits ausgeschnittenen Kartennummernbereich vor.
    /// Wichtig: Hier wird absichtlich NICHT binarisiert. Die Binarisierung
    /// erfolgt genau einmal im CharacterSegmenter.
    /// </summary>
    public static string Prepare(
        string imagePath)
    {
        if (!File.Exists(
                imagePath))
        {
            throw new FileNotFoundException(
                "Der OCR-Ausschnitt wurde nicht gefunden.",
                imagePath);
        }

        using Mat source =
            Cv2.ImRead(
                imagePath,
                ImreadModes.Color);

        if (source.Empty())
        {
            throw new InvalidOperationException(
                "Der OCR-Ausschnitt konnte nicht geladen werden.");
        }

        using Mat gray =
            new();

        Cv2.CvtColor(
            source,
            gray,
            ColorConversionCodes.BGR2GRAY);

        /*
         * Nur moderat vergrößern. 6x war unnötig teuer und hat die
         * Kanten durch die nachfolgenden Filter stärker verfälscht.
         */
        const double scaleFactor = 3.0;

        using Mat enlarged =
            new();

        Cv2.Resize(
            gray,
            enlarged,
            new Size(),
            scaleFactor,
            scaleFactor,
            InterpolationFlags.Cubic);

        /*
         * Leichte Kontrastnormalisierung, aber keine Schwellenwertbildung.
         * Dadurch bleiben die Originalkanten für den Segmenter erhalten.
         */
        using Mat normalized =
            new();

        Cv2.Normalize(
            enlarged,
            normalized,
            alpha: 0,
            beta: 255,
            normType: NormTypes.MinMax);

        using Mat padded =
            new();

        int horizontalPadding =
            Math.Max(
                12,
                normalized.Width / 30);

        int verticalPadding =
            Math.Max(
                8,
                normalized.Height / 8);

        Cv2.CopyMakeBorder(
            normalized,
            padded,
            top: verticalPadding,
            bottom: verticalPadding,
            left: horizontalPadding,
            right: horizontalPadding,
            borderType: BorderTypes.Constant,
            value: Scalar.White);

        string outputPath =
            Path.Combine(
                Path.GetTempPath(),
                $"onepiece-preprocessed-{Guid.NewGuid():N}.png");

        Cv2.ImWrite(
            outputPath,
            padded);

        return outputPath;
    }
}