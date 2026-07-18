using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceCardScanner.Recognition.Segmentation;

/// <summary>
/// Teilt ungewöhnlich breite OCR-Segmente automatisch in zwei oder drei Zeichen.
/// Die Methode übernimmt die übergebenen Segmente. Ersetzte Segmentbilder werden
/// dabei freigegeben; der Aufrufer muss nur die zurückgegebenen Segmente entsorgen.
/// </summary>
public sealed class WideSegmentSplitter
{
    private const int TargetWidth = 32;
    private const int TargetHeight = 40;
    private const int CanvasPadding = 3;

    public IReadOnlyList<CharacterSegment> SplitWideSegments(
        IReadOnlyList<CharacterSegment> segments)
    {
        if (segments.Count == 0)
        {
            return [];
        }

        double medianWidth = CalculateMedianCharacterWidth(segments);
        double medianHeight = CalculateMedianCharacterHeight(segments);

        var result = new List<CharacterSegment>();

        foreach (CharacterSegment segment in segments)
        {
            int desiredParts = EstimatePartCount(
                segment,
                medianWidth,
                medianHeight);

            if (desiredParts <= 1 ||
                !TrySplit(segment, desiredParts, out List<CharacterSegment> parts))
            {
                result.Add(segment);
                continue;
            }

            segment.Image.Dispose();
            result.AddRange(parts);
        }

        for (int index = 0; index < result.Count; index++)
        {
            result[index].Position = index;
        }

        return result;
    }

    private static int EstimatePartCount(
        CharacterSegment segment,
        double medianWidth,
        double medianHeight)
    {
        if (segment.Image.Empty() ||
            segment.Bounds.Width <= 0 ||
            segment.Bounds.Height <= 0)
        {
            return 1;
        }

        // Flache Segmente sind sehr wahrscheinlich der Bindestrich.
        if (segment.Bounds.Height < medianHeight * 0.55)
        {
            return 1;
        }

        double widthRatio =
            segment.Bounds.Width /
            Math.Max(1.0, medianWidth);

        if (widthRatio >= 2.35)
        {
            return 3;
        }

        if (widthRatio >= 1.48)
        {
            return 2;
        }

        return 1;
    }

    private static bool TrySplit(
        CharacterSegment source,
        int desiredParts,
        out List<CharacterSegment> parts)
    {
        parts = [];

        using Mat binary = EnsureWhiteInkOnBlack(source.Image);

        Rect inkBounds = FindInkBounds(binary);

        if (inkBounds.Width < desiredParts * 4 ||
            inkBounds.Height < 6)
        {
            return false;
        }

        int[] projection = BuildVerticalProjection(binary, inkBounds);
        double[] smoothed = Smooth(projection);

        List<int>? cuts = desiredParts switch
        {
            2 => FindTwoPartCuts(smoothed, inkBounds.Width),
            3 => FindThreePartCuts(smoothed, inkBounds.Width),
            _ => null
        };

        if (cuts == null || cuts.Count != desiredParts - 1)
        {
            return false;
        }

        var boundaries = new List<int> { 0 };
        boundaries.AddRange(cuts);
        boundaries.Add(inkBounds.Width);

        for (int index = 0; index < desiredParts; index++)
        {
            int left = boundaries[index];
            int right = boundaries[index + 1];

            if (right - left < 3)
            {
                Dispose(parts);
                parts = [];
                return false;
            }

            Rect localBounds = new(
                inkBounds.X + left,
                inkBounds.Y,
                right - left,
                inkBounds.Height);

            using Mat piece = new(binary, localBounds);

            if (Cv2.CountNonZero(piece) < 8)
            {
                Dispose(parts);
                parts = [];
                return false;
            }

            Mat normalized = NormalizeCharacter(piece);

            double leftRatio = left / (double)inkBounds.Width;
            double rightRatio = right / (double)inkBounds.Width;

            int originalLeft = source.Bounds.X +
                (int)Math.Round(source.Bounds.Width * leftRatio);

            int originalRight = source.Bounds.X +
                (int)Math.Round(source.Bounds.Width * rightRatio);

            parts.Add(
                new CharacterSegment
                {
                    Position = source.Position + index,
                    Bounds = new Rect(
                        originalLeft,
                        source.Bounds.Y,
                        Math.Max(1, originalRight - originalLeft),
                        source.Bounds.Height),
                    Image = normalized
                });
        }

        // Ein brauchbarer Split muss die Breiten einigermaßen ausbalancieren.
        int minimumWidth = parts.Min(part => part.Bounds.Width);
        int maximumWidth = parts.Max(part => part.Bounds.Width);

        if (minimumWidth <= 0 ||
            maximumWidth > minimumWidth * 2.8)
        {
            Dispose(parts);
            parts = [];
            return false;
        }

        return true;
    }

    private static List<int>? FindTwoPartCuts(
        IReadOnlyList<double> projection,
        int width)
    {
        int expected = width / 2;
        int minimum = Math.Max(3, (int)Math.Round(width * 0.28));
        int maximum = Math.Min(width - 3, (int)Math.Round(width * 0.72));

        if (minimum > maximum)
        {
            return null;
        }

        int cut = Enumerable.Range(minimum, maximum - minimum + 1)
            .OrderBy(column => CutCost(projection, column, expected, width))
            .First();

        if (!IsValleyStrongEnough(projection, cut))
        {
            return null;
        }

        return [cut];
    }

    private static List<int>? FindThreePartCuts(
        IReadOnlyList<double> projection,
        int width)
    {
        int expectedFirst = width / 3;
        int expectedSecond = width * 2 / 3;

        int firstMinimum = Math.Max(3, (int)Math.Round(width * 0.18));
        int firstMaximum = Math.Min(width - 7, (int)Math.Round(width * 0.46));
        int secondMinimum = Math.Max(7, (int)Math.Round(width * 0.54));
        int secondMaximum = Math.Min(width - 3, (int)Math.Round(width * 0.82));

        if (firstMinimum > firstMaximum ||
            secondMinimum > secondMaximum)
        {
            return null;
        }

        double bestCost = double.PositiveInfinity;
        int bestFirst = -1;
        int bestSecond = -1;

        for (int first = firstMinimum; first <= firstMaximum; first++)
        {
            for (int second = secondMinimum; second <= secondMaximum; second++)
            {
                if (second - first < 4)
                {
                    continue;
                }

                double cost =
                    CutCost(projection, first, expectedFirst, width) +
                    CutCost(projection, second, expectedSecond, width);

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestFirst = first;
                    bestSecond = second;
                }
            }
        }

        if (bestFirst < 0 || bestSecond < 0 ||
            !IsValleyStrongEnough(projection, bestFirst) ||
            !IsValleyStrongEnough(projection, bestSecond))
        {
            return null;
        }

        return [bestFirst, bestSecond];
    }

    private static double CutCost(
        IReadOnlyList<double> projection,
        int column,
        int expected,
        int width)
    {
        double normalizedInk = projection[column] /
            Math.Max(1.0, projection.Max());

        double positionPenalty =
            Math.Abs(column - expected) /
            Math.Max(1.0, width);

        return normalizedInk * 0.82 + positionPenalty * 0.18;
    }

    private static bool IsValleyStrongEnough(
        IReadOnlyList<double> projection,
        int cut)
    {
        double maximum = Math.Max(1.0, projection.Max());
        double local = projection[cut];

        // Auch berührende Zeichen dürfen getrennt werden; das Tal muss nicht leer sein.
        return local <= maximum * 0.62;
    }

    private static int[] BuildVerticalProjection(
        Mat binary,
        Rect bounds)
    {
        var projection = new int[bounds.Width];

        for (int x = 0; x < bounds.Width; x++)
        {
            using Mat column = new(
                binary,
                new Rect(bounds.X + x, bounds.Y, 1, bounds.Height));

            projection[x] = Cv2.CountNonZero(column);
        }

        return projection;
    }

    private static double[] Smooth(IReadOnlyList<int> values)
    {
        var result = new double[values.Count];

        for (int index = 0; index < values.Count; index++)
        {
            int from = Math.Max(0, index - 1);
            int to = Math.Min(values.Count - 1, index + 1);

            double sum = 0;
            int count = 0;

            for (int sample = from; sample <= to; sample++)
            {
                sum += values[sample];
                count++;
            }

            result[index] = sum / Math.Max(1, count);
        }

        return result;
    }

    private static Mat EnsureWhiteInkOnBlack(Mat source)
    {
        Mat gray = new();

        if (source.Channels() == 1)
        {
            source.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        }

        Cv2.Threshold(
            gray,
            gray,
            0,
            255,
            ThresholdTypes.Binary | ThresholdTypes.Otsu);

        int whitePixels = Cv2.CountNonZero(gray);
        int totalPixels = Math.Max(1, gray.Width * gray.Height);

        if (whitePixels > totalPixels / 2)
        {
            Cv2.BitwiseNot(gray, gray);
        }

        return gray;
    }

    private static Rect FindInkBounds(Mat image)
    {
        Point[][] contours = Cv2.FindContoursAsArray(
            image,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            return new Rect(0, 0, image.Width, image.Height);
        }

        return Cv2.BoundingRect(contours.SelectMany(contour => contour));
    }

    private static Mat NormalizeCharacter(Mat character)
    {
        Rect inkBounds = FindInkBounds(character);
        using Mat cropped = new(character, inkBounds);

        Mat canvas = new(
            TargetHeight,
            TargetWidth,
            MatType.CV_8UC1,
            Scalar.Black);

        double scale = Math.Min(
            (TargetWidth - CanvasPadding * 2.0) / cropped.Width,
            (TargetHeight - CanvasPadding * 2.0) / cropped.Height);

        int resizedWidth = Math.Max(
            1,
            (int)Math.Round(cropped.Width * scale));

        int resizedHeight = Math.Max(
            1,
            (int)Math.Round(cropped.Height * scale));

        using Mat resized = new();

        Cv2.Resize(
            cropped,
            resized,
            new Size(resizedWidth, resizedHeight),
            0,
            0,
            InterpolationFlags.Area);

        int x = (TargetWidth - resizedWidth) / 2;
        int y = (TargetHeight - resizedHeight) / 2;

        using Mat target = new(
            canvas,
            new Rect(x, y, resizedWidth, resizedHeight));

        resized.CopyTo(target);

        return canvas;
    }

    private static double CalculateMedianCharacterWidth(
        IReadOnlyList<CharacterSegment> segments)
    {
        List<int> widths = segments
            .Where(segment => segment.Bounds.Height >= 8)
            .Where(segment =>
                segment.Bounds.Width /
                (double)Math.Max(1, segment.Bounds.Height) < 1.45)
            .Select(segment => segment.Bounds.Width)
            .Where(width => width > 0)
            .OrderBy(width => width)
            .ToList();

        if (widths.Count == 0)
        {
            widths = segments
                .Select(segment => segment.Bounds.Width)
                .Where(width => width > 0)
                .OrderBy(width => width)
                .ToList();
        }

        return widths.Count == 0
            ? 1
            : widths[widths.Count / 2];
    }

    private static double CalculateMedianCharacterHeight(
        IReadOnlyList<CharacterSegment> segments)
    {
        List<int> heights = segments
            .Select(segment => segment.Bounds.Height)
            .Where(height => height > 0)
            .OrderBy(height => height)
            .ToList();

        return heights.Count == 0
            ? 1
            : heights[heights.Count / 2];
    }

    private static void Dispose(IEnumerable<CharacterSegment> segments)
    {
        foreach (CharacterSegment segment in segments)
        {
            segment.Image.Dispose();
        }
    }
}