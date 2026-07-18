using System;
using OpenCvSharp;

namespace OnePieceCardScanner.Recognition;

public sealed class CharacterNormalizer
{
    private const int TargetWidth = 40;
    private const int TargetHeight = 56;
    private const int Padding = 3;

    public Mat Normalize(Mat source)
    {
        if (source == null ||
            source.Empty())
        {
            throw new ArgumentException(
                "Das Zeichenbild ist leer.",
                nameof(source));
        }

        using Mat gray = new();

        if (source.Channels() == 1)
        {
            source.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(
                source,
                gray,
                ColorConversionCodes.BGR2GRAY);
        }

        using Mat binary = new();

        Cv2.Threshold(
            gray,
            binary,
            0,
            255,
            ThresholdTypes.Binary |
            ThresholdTypes.Otsu);

        int whitePixels =
            Cv2.CountNonZero(binary);

        int totalPixels =
            binary.Rows *
            binary.Cols;

        if (whitePixels >
            totalPixels / 2)
        {
            Cv2.BitwiseNot(
                binary,
                binary);
        }

        Point[][] contours =
            Cv2.FindContoursAsArray(
                binary,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            return CreateEmptyCanvas();
        }

        Rect bounds =
            Cv2.BoundingRect(
                contours.SelectMany(
                    contour => contour));

        bounds =
            AddSafePadding(
                bounds,
                binary,
                1);

        using Mat cropped =
            new Mat(
                binary,
                bounds);

        double scale =
            Math.Min(
                (TargetWidth - Padding * 2.0) /
                cropped.Width,

                (TargetHeight - Padding * 2.0) /
                cropped.Height);

        int resizedWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    cropped.Width * scale));

        int resizedHeight =
            Math.Max(
                1,
                (int)Math.Round(
                    cropped.Height * scale));

        using Mat resized = new();

        Cv2.Resize(
            cropped,
            resized,
            new Size(
                resizedWidth,
                resizedHeight),
            0,
            0,
            InterpolationFlags.Area);

        Mat canvas =
            CreateEmptyCanvas();

        int x =
            (TargetWidth - resizedWidth) / 2;

        int y =
            (TargetHeight - resizedHeight) / 2;

        using Mat destination =
            new Mat(
                canvas,
                new Rect(
                    x,
                    y,
                    resizedWidth,
                    resizedHeight));

        resized.CopyTo(
            destination);

        return canvas;
    }

    private static Mat CreateEmptyCanvas()
    {
        return new Mat(
            TargetHeight,
            TargetWidth,
            MatType.CV_8UC1,
            Scalar.Black);
    }

    private static Rect AddSafePadding(
        Rect bounds,
        Mat image,
        int padding)
    {
        int x =
            Math.Max(
                0,
                bounds.X - padding);

        int y =
            Math.Max(
                0,
                bounds.Y - padding);

        int right =
            Math.Min(
                image.Width,
                bounds.Right + padding);

        int bottom =
            Math.Min(
                image.Height,
                bounds.Bottom + padding);

        return new Rect(
            x,
            y,
            right - x,
            bottom - y);
    }
}