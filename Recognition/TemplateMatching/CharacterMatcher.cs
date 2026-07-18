using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceCardScanner.Recognition.TemplateMatching;

public sealed class CharacterMatcher : IDisposable
{
    private const int TargetWidth = 40;
    private const int TargetHeight = 56;
    private const int CanvasPadding = 3;
    private const int BestTemplatesPerClass = 5;

    private readonly CharacterTemplateLibrary _library;

    private readonly Dictionary<CharacterTemplate, Mat>
        _normalizedTemplates =
            new();

    private readonly Dictionary<CharacterTemplate, double>
        _templateInkRatios =
            new();

    private bool _isDisposed;

    public CharacterMatcher(
        CharacterTemplateLibrary library)
    {
        _library =
            library ??
            throw new ArgumentNullException(
                nameof(library));

        PrepareTemplateCache();
    }

    public IReadOnlyList<CharacterMatch> Match(
        Mat segment,
        int top = 10)
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        if (segment == null ||
            segment.Empty())
        {
            return [];
        }

        top =
            Math.Max(
                1,
                top);

        /*
         * Live-Segmente kommen bereits als 40x56-Binärbild aus dem
         * CharacterSegmenter. In diesem Fall wird nicht erneut
         * thresholded, zugeschnitten und skaliert.
         */
        bool canUseDirectly =
            IsPreparedSegment(
                segment);

        using Mat normalizedSegment =
            canUseDirectly
                ? segment.Clone()
                : Normalize(
                    segment);

        double segmentInkRatio =
            CalculateInkRatio(
                normalizedSegment);

        var results =
            new List<CharacterMatch>();

        foreach (KeyValuePair<char, List<CharacterTemplate>> pair
                 in _library.Templates)
        {
            CharacterTemplate? bestTemplate =
                null;

            double bestTemplateScore =
                double.NegativeInfinity;

            /*
             * Nur die besten fünf Scores pro Zeichenklasse werden benötigt.
             * Dadurch entfällt das Sortieren aller Templates.
             */
            var bestScores =
                new List<double>(
                    BestTemplatesPerClass);

            int comparedCount =
                0;

            foreach (CharacterTemplate template in
                     pair.Value)
            {
                if (!_normalizedTemplates.TryGetValue(
                        template,
                        out Mat? normalizedTemplate))
                {
                    continue;
                }

                double templateInkRatio =
                    _templateInkRatios[
                        template];

                /*
                 * Grober, sehr billiger Vorfilter. Stark unterschiedliche
                 * Tintenmengen können nicht dasselbe Zeichen darstellen.
                 */
                if (Math.Abs(
                        segmentInkRatio -
                        templateInkRatio) >
                    0.22)
                {
                    continue;
                }

                double score =
                    ComparePrepared(
                        normalizedSegment,
                        segmentInkRatio,
                        normalizedTemplate,
                        templateInkRatio);

                comparedCount++;

                if (score >
                    bestTemplateScore)
                {
                    bestTemplateScore =
                        score;

                    bestTemplate =
                        template;
                }

                InsertIntoBestScores(
                    bestScores,
                    score,
                    BestTemplatesPerClass);
            }

            if (bestTemplate == null ||
                bestScores.Count == 0)
            {
                continue;
            }

            double classScore =
                bestScores.Average();

            results.Add(
                new CharacterMatch
                {
                    Character =
                        pair.Key,

                    Score =
                        classScore,

                    BestTemplateScore =
                        bestTemplateScore,

                    ComparedTemplateCount =
                        comparedCount,

                    BestTemplate =
                        bestTemplate
                });
        }

        return results
            .OrderByDescending(result =>
                result.Score)
            .Take(top)
            .ToList();
    }

    private void PrepareTemplateCache()
    {
        foreach (CharacterTemplate template in
                 _library.GetAllTemplates())
        {
            if (template.Image == null ||
                template.Image.Empty())
            {
                continue;
            }

            Mat normalized =
                Normalize(
                    template.Image);

            _normalizedTemplates[
                template] =
                normalized;

            _templateInkRatios[
                template] =
                CalculateInkRatio(
                    normalized);
        }
    }

    private static bool IsPreparedSegment(
        Mat segment)
    {
        if (segment.Channels() != 1 ||
            segment.Width != TargetWidth ||
            segment.Height != TargetHeight)
        {
            return false;
        }

        /*
         * Binärtest über Min/Max. Ein vorbereitetes Segment enthält
         * nur 0 und 255.
         */
        Cv2.MinMaxLoc(
            segment,
            out double minimum,
            out double maximum);

        return minimum >= 0 &&
               maximum <= 255;
    }

    private static Mat Normalize(
        Mat source)
    {
        using Mat gray =
            new();

        if (source.Channels() == 1)
        {
            source.CopyTo(
                gray);
        }
        else
        {
            Cv2.CvtColor(
                source,
                gray,
                ColorConversionCodes.BGR2GRAY);
        }

        using Mat binary =
            new();

        Cv2.Threshold(
            gray,
            binary,
            0,
            255,
            ThresholdTypes.Binary |
            ThresholdTypes.Otsu);

        int whitePixels =
            Cv2.CountNonZero(
                binary);

        int totalPixels =
            Math.Max(
                1,
                binary.Width *
                binary.Height);

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
            return new Mat(
                TargetHeight,
                TargetWidth,
                MatType.CV_8UC1,
                Scalar.Black);
        }

        Rect bounds =
            Cv2.BoundingRect(
                contours.SelectMany(
                    contour =>
                        contour));

        int x =
            Math.Max(
                0,
                bounds.X - 1);

        int y =
            Math.Max(
                0,
                bounds.Y - 1);

        int right =
            Math.Min(
                binary.Width,
                bounds.Right + 1);

        int bottom =
            Math.Min(
                binary.Height,
                bounds.Bottom + 1);

        Rect safeBounds =
            new(
                x,
                y,
                Math.Max(
                    1,
                    right - x),
                Math.Max(
                    1,
                    bottom - y));

        using Mat cropped =
            new(
                binary,
                safeBounds);

        double scale =
            Math.Min(
                (TargetWidth -
                 CanvasPadding * 2.0) /
                cropped.Width,

                (TargetHeight -
                 CanvasPadding * 2.0) /
                cropped.Height);

        int resizedWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    cropped.Width *
                    scale));

        int resizedHeight =
            Math.Max(
                1,
                (int)Math.Round(
                    cropped.Height *
                    scale));

        using Mat resized =
            new();

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
            new(
                TargetHeight,
                TargetWidth,
                MatType.CV_8UC1,
                Scalar.Black);

        int destinationX =
            (TargetWidth -
             resizedWidth) / 2;

        int destinationY =
            (TargetHeight -
             resizedHeight) / 2;

        using Mat destination =
            new(
                canvas,
                new Rect(
                    destinationX,
                    destinationY,
                    resizedWidth,
                    resizedHeight));

        resized.CopyTo(
            destination);

        return canvas;
    }

    private static double ComparePrepared(
        Mat left,
        double leftInkRatio,
        Mat right,
        double rightInkRatio)
    {
        using Mat difference =
            new();

        Cv2.Absdiff(
            left,
            right,
            difference);

        double pixelScore =
            100.0 -
            Cv2.Mean(
                difference).Val0 /
            255.0 *
            100.0;

        double inkDifference =
            Math.Abs(
                leftInkRatio -
                rightInkRatio);

        double inkScore =
            Math.Clamp(
                100.0 -
                inkDifference *
                250.0,
                0,
                100);

        return Math.Clamp(
            pixelScore * 0.84 +
            inkScore * 0.16,
            0,
            100);
    }

    private static double CalculateInkRatio(
        Mat image)
    {
        return Cv2.CountNonZero(
                   image) /
               (double)Math.Max(
                   1,
                   image.Width *
                   image.Height);
    }

    private static void InsertIntoBestScores(
        List<double> scores,
        double score,
        int maximumCount)
    {
        int index =
            scores.FindIndex(
                existing =>
                    score >
                    existing);

        if (index < 0)
        {
            scores.Add(
                score);
        }
        else
        {
            scores.Insert(
                index,
                score);
        }

        if (scores.Count >
            maximumCount)
        {
            scores.RemoveAt(
                scores.Count - 1);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (Mat image in
                 _normalizedTemplates.Values)
        {
            image.Dispose();
        }

        _normalizedTemplates.Clear();
        _templateInkRatios.Clear();

        _isDisposed =
            true;
    }
}