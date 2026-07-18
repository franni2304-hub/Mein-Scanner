using System;
using OpenCvSharp;

namespace OnePieceCardScanner.Recognition;

public sealed class CardImageMatcher
{
    public double Compare(
        string scannedImagePath,
        string referenceImagePath)
    {
        using Mat scanned =
            Cv2.ImRead(
                scannedImagePath,
                ImreadModes.Color);

        using Mat reference =
            Cv2.ImRead(
                referenceImagePath,
                ImreadModes.Color);

        if (scanned.Empty() ||
            reference.Empty())
        {
            return 0;
        }

        Cv2.Resize(
            scanned,
            scanned,
            new Size(744, 1039));

        Cv2.Resize(
            reference,
            reference,
            new Size(744, 1039));

        Rect artwork =
            new(
                45,
                75,
                655,
                610);

        using Mat scanArtwork =
            new(scanned, artwork);

        using Mat referenceArtwork =
            new(reference, artwork);

        using Mat scanGray =
            new();

        using Mat referenceGray =
            new();

        Cv2.CvtColor(
            scanArtwork,
            scanGray,
            ColorConversionCodes.BGR2GRAY);

        Cv2.CvtColor(
            referenceArtwork,
            referenceGray,
            ColorConversionCodes.BGR2GRAY);

        Cv2.EqualizeHist(
            scanGray,
            scanGray);

        Cv2.EqualizeHist(
            referenceGray,
            referenceGray);

        using Mat difference =
            new();

        Cv2.Absdiff(
            scanGray,
            referenceGray,
            difference);

        Scalar mean =
            Cv2.Mean(
                difference);

        double similarity =
            100.0 -
            (mean.Val0 / 255.0 * 100.0);

        return Math.Clamp(
            similarity,
            0,
            100);
    }
}