using System;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace OnePieceCardScanner.Recognition;

public static class CardDetector
{
    private const int TargetWidth = 744;
    private const int TargetHeight = 1039;

    public static string DetectAndCropCard(string imagePath)
    {
        Point[]? cardContour =
            DetectWithEdges(imagePath);

        if (cardContour == null)
        {
            cardContour =
                DetectWithThreshold(imagePath);
        }

        if (cardContour == null)
        {
            throw new InvalidOperationException(
                "Es konnte keine Karte erkannt werden.");
        }

        return WarpCard(
            imagePath,
            cardContour);
    }

    private static Point[]? DetectWithEdges(
    string imagePath)
    {
        using var original = Cv2.ImRead(
            imagePath,
            ImreadModes.Color);

        if (original.Empty())
        {
            throw new InvalidOperationException(
                "Das Bild konnte nicht geladen werden.");
        }

        using var gray = new Mat();

        Cv2.CvtColor(
            original,
            gray,
            ColorConversionCodes.BGR2GRAY);

        using var blurred = new Mat();

        Cv2.GaussianBlur(
            gray,
            blurred,
            new Size(7, 7),
            0);

        using var edges = new Mat();

        Cv2.Canny(
            blurred,
            edges,
            30,
            100);

        using var kernel =
            Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(7, 7));

        Cv2.MorphologyEx(
            edges,
            edges,
            MorphTypes.Close,
            kernel,
            iterations: 3);

        Cv2.FindContours(
            edges,
            out Point[][] contours,
            out _,
            RetrievalModes.List,
            ContourApproximationModes.ApproxSimple);

        double imageArea =
            original.Width * original.Height;

        double minimumCardArea =
            imageArea * 0.01;

        Point[]? bestCardContour = null;
        double bestArea = 0;

        foreach (Point[] contour in contours)
        {
            double contourArea =
                Math.Abs(
                    Cv2.ContourArea(contour));

            if (contourArea < minimumCardArea)
                continue;

            double perimeter =
                Cv2.ArcLength(
                    contour,
                    true);

            Point[] approximatedContour =
                Cv2.ApproxPolyDP(
                    contour,
                    perimeter * 0.02,
                    true);

            if (approximatedContour.Length != 4)
                continue;

            if (!Cv2.IsContourConvex(
                approximatedContour))
            {
                continue;
            }

            RotatedRect rectangle =
                Cv2.MinAreaRect(
                    approximatedContour);

            double shortSide =
                Math.Min(
                    rectangle.Size.Width,
                    rectangle.Size.Height);

            double longSide =
                Math.Max(
                    rectangle.Size.Width,
                    rectangle.Size.Height);

            if (longSide <= 0)
                continue;

            double ratio =
                shortSide / longSide;

            if (ratio < 0.58 ||
                ratio > 0.82)
            {
                continue;
            }

            if (contourArea > bestArea)
            {
                bestArea = contourArea;
                bestCardContour =
                    approximatedContour;
            }
        }

        return bestCardContour;
    }

    private static Point[]? DetectWithThreshold(
    string imagePath)
    {
        using var original = Cv2.ImRead(
            imagePath,
            ImreadModes.Color);

        if (original.Empty())
            return null;

        using var gray = new Mat();

        Cv2.CvtColor(
            original,
            gray,
            ColorConversionCodes.BGR2GRAY);

        using var threshold = new Mat();

        Cv2.Threshold(
            gray,
            threshold,
            245,
            255,
            ThresholdTypes.BinaryInv);

        using var kernel =
            Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(9, 9));

        Cv2.MorphologyEx(
            threshold,
            threshold,
            MorphTypes.Close,
            kernel,
            iterations: 3);

        Cv2.FindContours(
            threshold,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        double imageArea =
            original.Width * original.Height;

        double minimumArea =
            imageArea * 0.01;

        Point[]? bestContour = null;
        double bestArea = 0;

        foreach (Point[] contour in contours)
        {
            double area =
                Math.Abs(
                    Cv2.ContourArea(contour));

            if (area < minimumArea)
                continue;

            RotatedRect rect =
                Cv2.MinAreaRect(contour);

            double shortSide =
                Math.Min(
                    rect.Size.Width,
                    rect.Size.Height);

            double longSide =
                Math.Max(
                    rect.Size.Width,
                    rect.Size.Height);

            if (longSide <= 0)
                continue;

            double ratio =
                shortSide / longSide;

            if (ratio < 0.58 ||
                ratio > 0.82)
            {
                continue;
            }

            Point2f[] points =
                rect.Points();

            Point[] polygon =
                points
                    .Select(point =>
                        new Point(
                            (int)point.X,
                            (int)point.Y))
                    .ToArray();

            if (area > bestArea)
            {
                bestArea = area;
                bestContour = polygon;
            }
        }

        return bestContour;
    }

    private static string WarpCard(
    string imagePath,
    Point[] contour)
    {
        using var original = Cv2.ImRead(
            imagePath,
            ImreadModes.Color);

        Point2f[] sourcePoints =
            OrderPoints(contour);

        const int targetWidth = 744;
        const int targetHeight = 1039;

        Point2f[] destinationPoints =
        [
            new Point2f(0, 0),
        new Point2f(targetWidth - 1, 0),
        new Point2f(
            targetWidth - 1,
            targetHeight - 1),
        new Point2f(
            0,
            targetHeight - 1)
        ];

        using Mat perspectiveMatrix =
            Cv2.GetPerspectiveTransform(
                sourcePoints,
                destinationPoints);

        using var correctedCard = new Mat();

        Cv2.WarpPerspective(
            original,
            correctedCard,
            perspectiveMatrix,
            new Size(
                targetWidth,
                targetHeight),
            InterpolationFlags.Cubic,
            BorderTypes.Constant,
            Scalar.White);

        using Mat orientedCard =
            EnsurePortraitOrientation(
                correctedCard);

        string outputPath = Path.Combine(
            Path.GetTempPath(),
            "onepiece-detected-card.png");

        Cv2.ImWrite(
            outputPath,
            orientedCard);

        return outputPath;
    }


    private static Point2f[] OrderPoints(
        Point[] points)
    {
        Point2f[] convertedPoints =
            points
                .Select(point =>
                    new Point2f(
                        point.X,
                        point.Y))
                .ToArray();

        Point2f topLeft =
            convertedPoints
                .OrderBy(point =>
                    point.X + point.Y)
                .First();

        Point2f bottomRight =
            convertedPoints
                .OrderByDescending(point =>
                    point.X + point.Y)
                .First();

        Point2f topRight =
            convertedPoints
                .OrderBy(point =>
                    point.Y - point.X)
                .First();

        Point2f bottomLeft =
            convertedPoints
                .OrderByDescending(point =>
                    point.Y - point.X)
                .First();

        return
        [
            topLeft,
            topRight,
            bottomRight,
            bottomLeft
        ];
    }
    private static Mat EnsurePortraitOrientation(Mat card)
    {
        if (card.Height >= card.Width)
        {
            return card.Clone();
        }

        var rotated = new Mat();

        Cv2.Rotate(
            card,
            rotated,
            RotateFlags.Rotate90Clockwise);

        return rotated;
    }
}