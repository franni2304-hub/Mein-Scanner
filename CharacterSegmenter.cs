using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceCardScanner.Recognition.Segmentation;

public sealed class CharacterSegmenter
{
    private const int TargetWidth = 40;
    private const int TargetHeight = 56;
    private const int CanvasPadding = 3;

    public IReadOnlyList<CharacterSegment> Segment(
        string imagePath)
    {
        using Mat source =
            Cv2.ImRead(
                imagePath,
                ImreadModes.Grayscale);

        if (source.Empty())
        {
            throw new InvalidOperationException(
                "Das Bild für die Zeichentrennung konnte nicht geladen werden.");
        }

        return Segment(
            source);
    }

    public IReadOnlyList<CharacterSegment> Segment(
        Mat source)
    {
        IReadOnlyList<SegmentationHypothesis> hypotheses =
            GenerateHypotheses(
                source,
                maximumHypotheses: 12);

        if (hypotheses.Count == 0)
        {
            return [];
        }

        SegmentationHypothesis best =
            hypotheses[0];

        var result =
            new List<CharacterSegment>();

        foreach (CharacterSegment segment in
                 best.Segments)
        {
            result.Add(
                new CharacterSegment
                {
                    Position =
                        segment.Position,

                    Bounds =
                        segment.Bounds,

                    Image =
                        segment.Image.Clone()
                });
        }

        foreach (SegmentationHypothesis hypothesis in
                 hypotheses)
        {
            hypothesis.Dispose();
        }

        return result;
    }

    public IReadOnlyList<SegmentationHypothesis> GenerateHypotheses(
        string imagePath,
        int maximumHypotheses = 16)
    {
        using Mat source =
            Cv2.ImRead(
                imagePath,
                ImreadModes.Grayscale);

        if (source.Empty())
        {
            throw new InvalidOperationException(
                "Das Bild für die Zeichentrennung konnte nicht geladen werden.");
        }

        return GenerateHypotheses(
            source,
            maximumHypotheses);
    }

    public IReadOnlyList<SegmentationHypothesis> GenerateHypotheses(
        Mat source,
        int maximumHypotheses = 16)
    {
        if (source == null ||
            source.Empty())
        {
            return [];
        }

        maximumHypotheses =
            Math.Clamp(
                maximumHypotheses,
                1,
                40);

        using Mat binary =
            CreateBinaryImage(
                source);

        Cv2.FindContours(
            binary,
            out Point[][] contours,
            out HierarchyIndex[] hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        List<Rect> rawRectangles =
            contours
                .Select(Cv2.BoundingRect)
                .Where(bounds =>
                    IsPossibleCharacter(
                        bounds,
                        binary.Size()))
                .OrderBy(bounds =>
                    bounds.X)
                .ToList();

        List<Rect> mergedRectangles =
            MergeNearbyRectangles(
                rawRectangles)
            .OrderBy(bounds =>
                bounds.X)
            .ToList();

        var candidates =
            new List<RectangleHypothesis>();

        AddRectangleHypothesis(
            candidates,
            mergedRectangles,
            "merged-contours",
            structuralBonus: 0.0);

        List<Rect> recoveredPrb =
            TryRecoverMergedPrbPrefix(
                binary,
                mergedRectangles);

        AddRectangleHypothesis(
            candidates,
            recoveredPrb,
            "prb-recovery",
            structuralBonus:
                recoveredPrb.Count != mergedRectangles.Count
                    ? 8.0
                    : -1.0);

        List<Rect> selected =
            SelectLikelyCardNumberSequence(
                recoveredPrb);

        AddRectangleHypothesis(
            candidates,
            selected,
            "legacy-best-sequence",
            structuralBonus: 10.0);

        AddWindowHypotheses(
            candidates,
            recoveredPrb);

        AddSplitHypotheses(
            binary,
            candidates,
            recoveredPrb);

        AddFragmentMergeHypotheses(
            candidates,
            recoveredPrb);

        List<RectangleHypothesis> ranked =
            DeduplicateRectangleHypotheses(
                candidates)
            .Select(candidate =>
            {
                candidate.GeometryScore =
                    ScoreSegmentationGeometry(
                        candidate.Rectangles,
                        binary.Size()) +
                    candidate.StructuralBonus;

                return candidate;
            })
            .OrderByDescending(candidate =>
                candidate.GeometryScore)
            .ThenBy(candidate =>
                Math.Abs(
                    candidate.Rectangles.Count - 8))
            .Take(maximumHypotheses)
            .ToList();

        var hypotheses =
            new List<SegmentationHypothesis>();

        for (int index = 0;
             index < ranked.Count;
             index++)
        {
            RectangleHypothesis candidate =
                ranked[index];

            IReadOnlyList<CharacterSegment> segments =
                CreateSegments(
                    binary,
                    candidate.Rectangles);

            if (segments.Count == 0)
            {
                continue;
            }

            hypotheses.Add(
                new SegmentationHypothesis(
                    sourceName:
                        candidate.SourceName,
                    geometryScore:
                        candidate.GeometryScore,
                    segments:
                        segments));
        }

        return hypotheses;
    }

    private static IReadOnlyList<CharacterSegment> CreateSegments(
        Mat binary,
        IReadOnlyList<Rect> rectangles)
    {
        var segments =
            new List<CharacterSegment>();

        for (int index = 0;
             index < rectangles.Count;
             index++)
        {
            Rect bounds =
                AddPadding(
                    rectangles[index],
                    binary,
                    padding: 2);

            using Mat cropped =
                new(
                    binary,
                    bounds);

            Mat normalized =
                NormalizeCharacter(
                    cropped);

            segments.Add(
                new CharacterSegment
                {
                    Position =
                        index,

                    Bounds =
                        bounds,

                    Image =
                        normalized
                });
        }

        return segments;
    }

    private static void AddWindowHypotheses(
        ICollection<RectangleHypothesis> output,
        IReadOnlyList<Rect> rectangles)
    {
        AddWindowsForFormat(
            output,
            rectangles,
            length: 5,
            dashIndex: 1,
            sourcePrefix: "promo-window");

        AddWindowsForFormat(
            output,
            rectangles,
            length: 8,
            dashIndex: 4,
            sourcePrefix: "standard-window");

        AddWindowsForFormat(
            output,
            rectangles,
            length: 9,
            dashIndex: 5,
            sourcePrefix: "prb-window");
    }

    private static void AddWindowsForFormat(
        ICollection<RectangleHypothesis> output,
        IReadOnlyList<Rect> rectangles,
        int length,
        int dashIndex,
        string sourcePrefix)
    {
        if (rectangles.Count < length)
        {
            return;
        }

        double medianHeight =
            GetMedian(
                rectangles.Select(bounds =>
                    bounds.Height));

        for (int start = 0;
             start + length <= rectangles.Count;
             start++)
        {
            List<Rect> window =
                rectangles
                    .Skip(start)
                    .Take(length)
                    .ToList();

            bool dashPlausible =
                IsLikelyDashRelaxed(
                    window[dashIndex],
                    medianHeight);

            double bonus =
                dashPlausible
                    ? 14.0
                    : -12.0;

            bonus -=
                (rectangles.Count - length) *
                1.5;

            bonus +=
                ScoreDiscardedComponents(
                    rectangles,
                    start,
                    length,
                    medianHeight) *
                1.5;

            if (length == 5)
            {
                int contiguousPrefixCharacters =
                    CountContiguousDiscardedPrefixCharacters(
                        rectangles,
                        start,
                        medianHeight);

                bonus -=
                    contiguousPrefixCharacters *
                    12.0;

                if (contiguousPrefixCharacters >= 2)
                {
                    bonus -=
                        12.0;
                }
            }

            AddRectangleHypothesis(
                output,
                window,
                $"{sourcePrefix}-{start}",
                bonus);
        }
    }

    private static int CountContiguousDiscardedPrefixCharacters(
        IReadOnlyList<Rect> rectangles,
        int startIndex,
        double medianHeight)
    {
        if (startIndex <= 0 ||
            medianHeight <= 0)
        {
            return 0;
        }

        int count =
            0;

        Rect next =
            rectangles[startIndex];

        for (int index = startIndex - 1;
             index >= 0;
             index--)
        {
            Rect candidate =
                rectangles[index];

            int gap =
                next.Left -
                candidate.Right;

            double centerDistance =
                Math.Abs(
                    (candidate.Y + candidate.Height / 2.0) -
                    (next.Y + next.Height / 2.0));

            double heightRatio =
                candidate.Height /
                Math.Max(
                    1.0,
                    medianHeight);

            double aspectRatio =
                candidate.Width /
                (double)Math.Max(
                    1,
                    candidate.Height);

            bool contiguous =
                gap >= -medianHeight * 0.18 &&
                gap <= medianHeight * 0.55 &&
                centerDistance <= medianHeight * 0.48;

            bool characterLike =
                heightRatio >= 0.62 &&
                heightRatio <= 1.55 &&
                aspectRatio >= 0.06 &&
                aspectRatio <= 1.45;

            if (!contiguous ||
                !characterLike)
            {
                break;
            }

            count++;
            next = candidate;
        }

        return count;
    }

    private static void AddSplitHypotheses(
        Mat binary,
        ICollection<RectangleHypothesis> output,
        IReadOnlyList<Rect> rectangles)
    {
        if (rectangles.Count < 4 ||
            rectangles.Count > 10)
        {
            return;
        }

        double medianWidth =
            GetMedian(
                rectangles
                    .Where(bounds =>
                        bounds.Width /
                        (double)Math.Max(1, bounds.Height) <= 1.20)
                    .Select(bounds =>
                        bounds.Width));

        if (medianWidth <= 0)
        {
            medianWidth =
                GetMedian(
                    rectangles.Select(bounds =>
                        bounds.Width));
        }

        var viableSplits =
            new List<(int Index, Rect Left, Rect Right)>();

        for (int index = 0;
             index < rectangles.Count;
             index++)
        {
            Rect candidate =
                rectangles[index];

            if (candidate.Width <
                Math.Max(
                    9.0,
                    medianWidth * 1.45))
            {
                continue;
            }

            if (!TrySplitAtStrongProjectionValley(
                    binary,
                    candidate,
                    out Rect left,
                    out Rect right))
            {
                continue;
            }

            viableSplits.Add(
                (index, left, right));

            var split =
                rectangles.ToList();

            split.RemoveAt(index);
            split.Insert(index, right);
            split.Insert(index, left);

            AddRectangleHypothesis(
                output,
                split,
                $"projection-split-{index}",
                structuralBonus: 5.0);

            List<Rect> selected =
                SelectLikelyCardNumberSequence(
                    split);

            AddRectangleHypothesis(
                output,
                selected,
                $"projection-split-selected-{index}",
                structuralBonus: 8.0);
        }

        int compoundCount =
            0;

        for (int firstIndex = 0;
             firstIndex < viableSplits.Count && compoundCount < 6;
             firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < viableSplits.Count && compoundCount < 6;
                 secondIndex++)
            {
                var compound =
                    rectangles.ToList();

                foreach ((int index, Rect splitLeft, Rect splitRight) in
                         new[]
                         {
                             viableSplits[secondIndex],
                             viableSplits[firstIndex]
                         })
                {
                    compound.RemoveAt(index);
                    compound.Insert(index, splitRight);
                    compound.Insert(index, splitLeft);
                }

                AddRectangleHypothesis(
                    output,
                    compound,
                    $"projection-double-split-{viableSplits[firstIndex].Index}-{viableSplits[secondIndex].Index}",
                    structuralBonus: 7.0);

                AddRectangleHypothesis(
                    output,
                    SelectLikelyCardNumberSequence(compound),
                    $"projection-double-split-selected-{viableSplits[firstIndex].Index}-{viableSplits[secondIndex].Index}",
                    structuralBonus: 10.0);

                compoundCount++;
            }
        }
    }
    private static void AddFragmentMergeHypotheses(
        ICollection<RectangleHypothesis> output,
        IReadOnlyList<Rect> rectangles)
    {
        if (rectangles.Count < 6 ||
            rectangles.Count > 11)
        {
            return;
        }

        double medianHeight =
            GetMedian(
                rectangles.Select(bounds =>
                    bounds.Height));

        for (int index = 0;
             index + 1 < rectangles.Count;
             index++)
        {
            Rect left =
                rectangles[index];

            Rect right =
                rectangles[index + 1];

            int gap =
                right.Left -
                left.Right;

            int overlap =
                Math.Min(left.Bottom, right.Bottom) -
                Math.Max(left.Top, right.Top);

            double overlapRatio =
                overlap /
                (double)Math.Max(
                    1,
                    Math.Min(left.Height, right.Height));

            bool likelyFragments =
                gap <= Math.Max(2.0, medianHeight * 0.08) &&
                overlapRatio >= 0.30 &&
                (left.Width <= medianHeight * 0.28 ||
                 right.Width <= medianHeight * 0.28);

            if (!likelyFragments)
            {
                continue;
            }

            var merged =
                rectangles.ToList();

            merged[index] =
                Combine(
                    left,
                    right);

            merged.RemoveAt(
                index + 1);

            AddRectangleHypothesis(
                output,
                merged,
                $"fragment-merge-{index}",
                structuralBonus: 3.0);

            AddRectangleHypothesis(
                output,
                SelectLikelyCardNumberSequence(
                    merged),
                $"fragment-merge-selected-{index}",
                structuralBonus: 6.0);
        }

        AddTouchingPairMergeHypotheses(
            output,
            rectangles,
            medianHeight);
    }

    private static void AddTouchingPairMergeHypotheses(
        ICollection<RectangleHypothesis> output,
        IReadOnlyList<Rect> rectangles,
        double medianHeight)
    {
        double medianWidth =
            GetMedian(
                rectangles
                    .Where(bounds =>
                        bounds.Height >= medianHeight * 0.65 &&
                        !IsLikelyDashRelaxed(bounds, medianHeight))
                    .Select(bounds =>
                        bounds.Width));

        if (medianHeight <= 0 ||
            medianWidth <= 0)
        {
            return;
        }

        int added = 0;

        for (int index = 0;
             index + 1 < rectangles.Count && added < 4;
             index++)
        {
            Rect left = rectangles[index];
            Rect right = rectangles[index + 1];
            int gap = right.Left - left.Right;
            int overlap =
                Math.Min(left.Bottom, right.Bottom) -
                Math.Max(left.Top, right.Top);

            double overlapRatio =
                overlap /
                (double)Math.Max(1, Math.Min(left.Height, right.Height));

            Rect combined = Combine(left, right);

            bool plausiblePair =
                gap <= Math.Max(1.0, medianHeight * 0.05) &&
                gap >= -medianWidth * 0.35 &&
                overlapRatio >= 0.58 &&
                combined.Width <= medianWidth * 1.45 &&
                combined.Height <= medianHeight * 1.25 &&
                !IsLikelyDashRelaxed(left, medianHeight) &&
                !IsLikelyDashRelaxed(right, medianHeight);

            if (!plausiblePair)
            {
                continue;
            }

            var merged = rectangles.ToList();
            merged[index] = combined;
            merged.RemoveAt(index + 1);

            AddRectangleHypothesis(
                output,
                merged,
                $"touching-pair-merge-{index}",
                structuralBonus: 2.0);

            added++;
        }
    }

    private static void AddRectangleHypothesis(
        ICollection<RectangleHypothesis> output,
        IReadOnlyList<Rect> rectangles,
        string sourceName,
        double structuralBonus)
    {
        List<Rect> ordered =
            rectangles
                .Where(bounds =>
                    bounds.Width > 0 &&
                    bounds.Height > 0)
                .OrderBy(bounds =>
                    bounds.X)
                .ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        output.Add(
            new RectangleHypothesis
            {
                SourceName =
                    sourceName,

                Rectangles =
                    ordered,

                StructuralBonus =
                    structuralBonus
            });
    }

    private static IEnumerable<RectangleHypothesis> DeduplicateRectangleHypotheses(
        IEnumerable<RectangleHypothesis> candidates)
    {
        var seen =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (RectangleHypothesis candidate in
                 candidates)
        {
            string key =
                string.Join(
                    ";",
                    candidate.Rectangles.Select(bounds =>
                        $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}"));

            if (seen.Add(key))
            {
                yield return candidate;
            }
        }
    }

    private static double ScoreSegmentationGeometry(
        IReadOnlyList<Rect> rectangles,
        Size imageSize)
    {
        if (rectangles.Count == 0)
        {
            return double.NegativeInfinity;
        }

        double score =
            0.0;

        score +=
            rectangles.Count switch
            {
                5 => 26.0,
                8 => 34.0,
                9 => 32.0,
                6 or 7 or 10 => 6.0,
                _ => -18.0
            };

        double medianHeight =
            GetMedian(
                rectangles.Select(bounds =>
                    bounds.Height));

        double medianWidth =
            GetMedian(
                rectangles
                    .Where(bounds =>
                        bounds.Height >= medianHeight * 0.60)
                    .Select(bounds =>
                        bounds.Width));

        if (medianHeight <= 0)
        {
            return double.NegativeInfinity;
        }

        foreach (Rect bounds in
                 rectangles)
        {
            double heightDeviation =
                Math.Abs(
                    bounds.Height - medianHeight) /
                medianHeight;

            score -=
                Math.Min(
                    18.0,
                    heightDeviation * 13.0);

            double aspect =
                bounds.Width /
                (double)Math.Max(1, bounds.Height);

            if (aspect < 0.04 ||
                aspect > 2.70)
            {
                score -=
                    14.0;
            }
        }

        for (int index = 1;
             index < rectangles.Count;
             index++)
        {
            int gap =
                rectangles[index].Left -
                rectangles[index - 1].Right;

            if (gap > medianHeight * 0.95)
            {
                score -=
                    22.0;
            }
            else if (gap < -medianHeight * 0.22)
            {
                score -=
                    12.0;
            }
        }

        int expectedDashIndex =
            rectangles.Count switch
            {
                5 => 1,
                8 => 4,
                9 => 5,
                _ => -1
            };

        if (expectedDashIndex >= 0)
        {
            Rect dash =
                rectangles[expectedDashIndex];

            score +=
                IsLikelyDashRelaxed(
                    dash,
                    medianHeight)
                    ? 30.0
                    : -20.0;
        }

        Rect totalBounds =
            rectangles.Aggregate(
                Combine);

        double widthRatio =
            totalBounds.Width /
            (double)Math.Max(1, imageSize.Width);

        if (widthRatio < 0.20)
        {
            score -=
                15.0;
        }
        else if (widthRatio > 0.98)
        {
            score -=
                8.0;
        }
        else
        {
            score +=
                Math.Min(
                    12.0,
                    widthRatio * 16.0);
        }

        if (medianWidth > 0)
        {
            int extremeWidthCount =
                rectangles.Count(bounds =>
                    bounds.Width > medianWidth * 2.25 &&
                    !IsLikelyDashRelaxed(
                        bounds,
                        medianHeight));

            score -=
                extremeWidthCount * 10.0;
        }

        return score;
    }

    private sealed class RectangleHypothesis
    {
        public string SourceName { get; init; } =
            string.Empty;

        public List<Rect> Rectangles { get; init; } =
            [];

        public double StructuralBonus { get; init; }

        public double GeometryScore { get; set; }
    }

    private static Mat CreateBinaryImage(
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

        /*
         * Nur eine sehr leichte Glättung. Die vorherige doppelte
         * Verarbeitung hat dünne Zeichenstriche zerstört.
         */
        using Mat blurred =
            new();

        Cv2.GaussianBlur(
            gray,
            blurred,
            new Size(3, 3),
            0);

        Mat binary =
            new();

        Cv2.Threshold(
            blurred,
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
                binary.Rows *
                binary.Cols);

        /*
         * Für FindContours benötigen wir weiße Zeichen
         * auf schwarzem Hintergrund.
         */
        if (whitePixels >
            totalPixels / 2)
        {
            Cv2.BitwiseNot(
                binary,
                binary);
        }

        /*
         * Keine Morphology-Open-Operation mehr. Sie hat bei kleinen
         * Live-Ausschnitten Teile von 1, 3, 4, 6 und 9 entfernt.
         */
        return binary;
    }

    private static bool IsPossibleCharacter(
        Rect bounds,
        Size imageSize)
    {
        if (bounds.Width < 2 ||
            bounds.Height < 6)
        {
            return false;
        }

        if (bounds.Width >
            imageSize.Width * 0.38)
        {
            return false;
        }

        if (bounds.Height >
            imageSize.Height * 0.95)
        {
            return false;
        }

        int area =
            bounds.Width *
            bounds.Height;

        if (area < 14)
        {
            return false;
        }

        double aspectRatio =
            bounds.Width /
            (double)Math.Max(
                1,
                bounds.Height);

        bool couldBeDash =
            bounds.Height >= 2 &&
            aspectRatio >= 1.3 &&
            aspectRatio <= 10.0;

        bool couldBeNormalCharacter =
            aspectRatio >= 0.06 &&
            aspectRatio <= 2.6;

        return couldBeDash ||
               couldBeNormalCharacter;
    }

    private static List<Rect> MergeNearbyRectangles(
        IReadOnlyList<Rect> rectangles)
    {
        List<Rect> ordered =
            rectangles
                .OrderBy(rectangle =>
                    rectangle.X)
                .ToList();

        var merged =
            new List<Rect>();

        foreach (Rect rectangle in
                 ordered)
        {
            if (merged.Count == 0)
            {
                merged.Add(
                    rectangle);

                continue;
            }

            Rect previous =
                merged[^1];

            int horizontalGap =
                rectangle.Left -
                previous.Right;

            int verticalOverlap =
                Math.Min(
                    previous.Bottom,
                    rectangle.Bottom) -
                Math.Max(
                    previous.Top,
                    rectangle.Top);

            int minimumHeight =
                Math.Min(
                    previous.Height,
                    rectangle.Height);

            bool overlapsVertically =
                verticalOverlap >
                minimumHeight * 0.45;

            /*
             * Nur echte Teilkonturen desselben Zeichens verbinden.
             * Ein Gap von 2 Pixeln war nach 3x-Skalierung zu großzügig
             * und hat benachbarte Zeichen zusammengeführt.
             */
            bool shouldMerge =
                horizontalGap <= 1 &&
                overlapsVertically;

            if (shouldMerge)
            {
                merged[^1] =
                    Combine(
                        previous,
                        rectangle);
            }
            else
            {
                merged.Add(
                    rectangle);
            }
        }

        return merged;
    }

    private static List<Rect> TryRecoverMergedPrbPrefix(
        Mat binary,
        IReadOnlyList<Rect> rectangles)
    {
        List<Rect> ordered =
            rectangles
                .OrderBy(rectangle =>
                    rectangle.X)
                .ToList();

        if (ordered.Count != 8)
        {
            return ordered;
        }

        double medianHeight =
            GetMedian(
                ordered.Select(rectangle =>
                    rectangle.Height));

        double medianWidth =
            GetMedian(
                ordered
                    .Where(rectangle =>
                        rectangle.Width /
                        (double)Math.Max(
                            1,
                            rectangle.Height) <= 1.15)
                    .Select(rectangle =>
                        rectangle.Width));

        if (medianHeight <= 0 ||
            medianWidth <= 0)
        {
            return ordered;
        }

        int likelyDashIndex =
            ordered.FindIndex(rectangle =>
                IsLikelyDash(
                    rectangle,
                    medianHeight));

        if (likelyDashIndex < 3 ||
            likelyDashIndex > 5)
        {
            return ordered;
        }

        int searchEnd =
            Math.Min(
                likelyDashIndex,
                2);

        for (int index = 0;
             index < searchEnd;
             index++)
        {
            Rect candidate =
                ordered[index];

            double aspectRatio =
                candidate.Width /
                (double)Math.Max(
                    1,
                    candidate.Height);

            bool clearlyWide =
                candidate.Width >=
                    medianWidth * 1.55 &&
                candidate.Width >=
                    candidate.Height * 0.82 &&
                candidate.Height >=
                    medianHeight * 0.68 &&
                aspectRatio >= 0.82 &&
                aspectRatio <= 2.35;

            if (!clearlyWide ||
                !TrySplitAtStrongProjectionValley(
                    binary,
                    candidate,
                    out Rect left,
                    out Rect right))
            {
                continue;
            }

            var recovered =
                new List<Rect>();

            recovered.AddRange(
                ordered.Take(
                    index));

            recovered.Add(
                left);

            recovered.Add(
                right);

            recovered.AddRange(
                ordered.Skip(
                    index + 1));

            return recovered
                .OrderBy(rectangle =>
                    rectangle.X)
                .ToList();
        }

        return ordered;
    }

    private static bool TrySplitAtStrongProjectionValley(
        Mat binary,
        Rect bounds,
        out Rect left,
        out Rect right)
    {
        left =
            bounds;

        right =
            bounds;

        if (bounds.Width < 10 ||
            bounds.Height < 8)
        {
            return false;
        }

        using Mat roi =
            new(
                binary,
                bounds);

        int minimumSplit =
            Math.Max(
                2,
                (int)Math.Round(
                    roi.Width * 0.28));

        int maximumSplit =
            Math.Min(
                roi.Width - 3,
                (int)Math.Round(
                    roi.Width * 0.72));

        if (minimumSplit >=
            maximumSplit)
        {
            return false;
        }

        int bestX =
            -1;

        double bestValleyScore =
            double.PositiveInfinity;

        int maximumInk =
            0;

        for (int x = 0;
             x < roi.Width;
             x++)
        {
            using Mat column =
                roi.Col(
                    x);

            int ink =
                Cv2.CountNonZero(
                    column);

            maximumInk =
                Math.Max(
                    maximumInk,
                    ink);

            if (x >= minimumSplit &&
                x <= maximumSplit)
            {
                int neighborInk = ink;
                int neighborCount = 1;

                for (int offset = 1;
                     offset <= 2;
                     offset++)
                {
                    if (x - offset >= 0)
                    {
                        using Mat leftColumn = roi.Col(x - offset);
                        neighborInk += Cv2.CountNonZero(leftColumn);
                        neighborCount++;
                    }

                    if (x + offset < roi.Width)
                    {
                        using Mat rightColumn = roi.Col(x + offset);
                        neighborInk += Cv2.CountNonZero(rightColumn);
                        neighborCount++;
                    }
                }

                double balancePenalty =
                    Math.Abs(x - roi.Width / 2.0) /
                    Math.Max(1.0, roi.Width) *
                    Math.Max(1, maximumInk) *
                    0.18;

                double valleyScore =
                    neighborInk /
                    (double)neighborCount +
                    balancePenalty;

                if (valleyScore < bestValleyScore)
                {
                    bestValleyScore = valleyScore;
                    bestX = x;
                }
            }
        }

        if (bestX < 0 ||
            maximumInk <= 0 ||
            bestValleyScore >
                Math.Max(
                    1.25,
                    maximumInk * 0.34))
        {
            return false;
        }

        Rect rawLeft =
            new(
                bounds.X,
                bounds.Y,
                bestX,
                bounds.Height);

        int rightLocalX =
            bestX + 1;

        Rect rawRight =
            new(
                bounds.X +
                    rightLocalX,
                bounds.Y,
                bounds.Width -
                    rightLocalX,
                bounds.Height);

        if (!TryTightenToInk(
                binary,
                rawLeft,
                out left) ||
            !TryTightenToInk(
                binary,
                rawRight,
                out right))
        {
            left =
                bounds;

            right =
                bounds;

            return false;
        }

        double leftRatio =
            left.Width /
            (double)Math.Max(
                1,
                left.Height);

        double rightRatio =
            right.Width /
            (double)Math.Max(
                1,
                right.Height);

        return left.Width >= 2 &&
               right.Width >= 2 &&
               leftRatio >= 0.08 &&
               leftRatio <= 1.45 &&
               rightRatio >= 0.08 &&
               rightRatio <= 1.45;
    }

    private static bool TryTightenToInk(
        Mat binary,
        Rect bounds,
        out Rect tightened)
    {
        tightened =
            bounds;

        if (bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return false;
        }

        using Mat roi =
            new(
                binary,
                bounds);

        Point[][] contours =
            Cv2.FindContoursAsArray(
                roi,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            return false;
        }

        Rect[] localBounds =
            contours
                .Select(
                    Cv2.BoundingRect)
                .Where(rectangle =>
                    rectangle.Width > 0 &&
                    rectangle.Height > 0)
                .ToArray();

        if (localBounds.Length == 0)
        {
            return false;
        }

        int leftX =
            localBounds.Min(rectangle =>
                rectangle.Left);

        int topY =
            localBounds.Min(rectangle =>
                rectangle.Top);

        int rightX =
            localBounds.Max(rectangle =>
                rectangle.Right);

        int bottomY =
            localBounds.Max(rectangle =>
                rectangle.Bottom);

        tightened =
            new Rect(
                bounds.X +
                    leftX,
                bounds.Y +
                    topY,
                rightX -
                    leftX,
                bottomY -
                    topY);

        return tightened.Width > 0 &&
               tightened.Height > 0;
    }

    private static List<Rect> SelectLikelyCardNumberSequence(
        IReadOnlyList<Rect> rectangles)
    {
        List<Rect> ordered =
            rectangles
                .OrderBy(rectangle =>
                    rectangle.X)
                .ToList();

        if (ordered.Count < 5)
        {
            return ordered;
        }

        double medianCharacterHeight =
            GetMedian(
                ordered
                    .Where(rectangle =>
                        rectangle.Width /
                        (double)Math.Max(
                            1,
                            rectangle.Height) <= 1.30)
                    .Select(rectangle =>
                        rectangle.Height));

        if (medianCharacterHeight <= 0)
        {
            medianCharacterHeight =
                GetMedian(
                    ordered.Select(rectangle =>
                        rectangle.Height));
        }

        List<Rect>? bestSequence =
            null;

        double bestScore =
            double.NegativeInfinity;

        /*
         * PRBxx-xxx besitzt neun Zeichen. Dieses Format wird nur dann
         * akzeptiert, wenn der Ausschnitt exakt neun Komponenten besitzt und
         * die sechste Komponente geometrisch ein Bindestrich ist. Dadurch
         * kann ein normales achtstelliges Format mit einer Zusatzkomponente
         * nicht fälschlich als PRB interpretiert werden.
         */
        if (ordered.Count == 9)
        {
            List<Rect> prbSequence =
                ordered.ToList();

            Rect prbDash =
                prbSequence[5];

            if (IsLikelyDashRelaxed(
                    prbDash,
                    medianCharacterHeight))
            {
                double prbScore =
                    ScoreCardNumberSequence(
                        prbSequence,
                        dashIndex: 5,
                        medianCharacterHeight) +
                    20.0;

                if (prbScore >
                    bestScore)
                {
                    bestScore =
                        prbScore;

                    bestSequence =
                        prbSequence;
                }
            }
        }

        /*
         * Normale Kartennummern wie OP01-001, ST11-004 oder EB03-031
         * besitzen acht Zeichen. Bei zusätzlichen Symbolen werden alle
         * zusammenhängenden Acht-Fenster geprüft. Der erwartete Bindestrich
         * liegt an Position fünf.
         */
        if (ordered.Count >= 8)
        {
            const int normalLength = 8;
            const int normalDashIndex = 4;

            for (int startIndex = 0;
                 startIndex + normalLength <= ordered.Count;
                 startIndex++)
            {
                List<Rect> sequence =
                    ordered
                        .Skip(
                            startIndex)
                        .Take(
                            normalLength)
                        .ToList();

                Rect dash =
                    sequence[normalDashIndex];

                if (!IsLikelyDashRelaxed(
                        dash,
                        medianCharacterHeight))
                {
                    continue;
                }

                double score =
                    ScoreCardNumberSequence(
                        sequence,
                        normalDashIndex,
                        medianCharacterHeight);

                int discardedLeft =
                    startIndex;

                int discardedRight =
                    ordered.Count -
                    (startIndex + normalLength);

                /*
                 * Zusätzliche Komponenten sind erlaubt, aber nicht kostenlos.
                 * So können SR/SP/Rarity-Symbole entfernt werden, ohne dass
                 * beliebige Teilfolgen aus einer falschen Textzeile gewinnen.
                 */
                score -=
                    (discardedLeft +
                     discardedRight) *
                    2.5;

                if (ordered.Count ==
                    normalLength)
                {
                    score +=
                        18.0;
                }

                score +=
                    ScoreDiscardedComponents(
                        ordered,
                        startIndex,
                        normalLength,
                        medianCharacterHeight);

                if (score >
                    bestScore)
                {
                    bestScore =
                        score;

                    bestSequence =
                        sequence;
                }
            }
        }

        /*
         * Promo-Karten P-001 haben nur fünf Zeichen. Dieses kurze Format wird
         * weiterhin ausschließlich bei bereits kurzen Ausschnitten erlaubt.
         */
        if (ordered.Count >= 5 &&
            ordered.Count <= 6)
        {
            const int promoLength = 5;
            const int promoDashIndex = 1;

            for (int startIndex = 0;
                 startIndex + promoLength <= ordered.Count;
                 startIndex++)
            {
                List<Rect> sequence =
                    ordered
                        .Skip(
                            startIndex)
                        .Take(
                            promoLength)
                        .ToList();

                if (!IsLikelyDashRelaxed(
                        sequence[promoDashIndex],
                        medianCharacterHeight))
                {
                    continue;
                }

                double score =
                    ScoreCardNumberSequence(
                        sequence,
                        promoDashIndex,
                        medianCharacterHeight);

                score -=
                    (ordered.Count -
                     promoLength) *
                    4.0;

                if (score >
                    bestScore)
                {
                    bestScore =
                        score;

                    bestSequence =
                        sequence;
                }
            }
        }

        /*
         * Nur bei klarer geometrischer Evidenz beschneiden. Sonst bleiben
         * alle Komponenten erhalten und die übrigen Regionskandidaten können
         * weiterhin gegeneinander bewertet werden.
         */
        return bestSequence != null &&
               bestScore >= 38.0
            ? bestSequence
            : ordered;
    }

    private static double ScoreDiscardedComponents(
        IReadOnlyList<Rect> ordered,
        int startIndex,
        int selectedLength,
        double medianCharacterHeight)
    {
        double score =
            0.0;

        IEnumerable<Rect> discarded =
            ordered
                .Take(
                    startIndex)
                .Concat(
                    ordered.Skip(
                        startIndex +
                        selectedLength));

        foreach (Rect rectangle in
                 discarded)
        {
            double heightRatio =
                rectangle.Height /
                Math.Max(
                    1.0,
                    medianCharacterHeight);

            double aspectRatio =
                rectangle.Width /
                (double)Math.Max(
                    1,
                    rectangle.Height);

            /*
             * Sehr kleine, sehr große oder besonders breite Komponenten sind
             * typische Symbole und dürfen ohne großen Verlust entfernt werden.
             */
            if (heightRatio < 0.55 ||
                heightRatio > 1.65 ||
                aspectRatio > 1.55)
            {
                score +=
                    4.0;
            }
            else
            {
                score -=
                    2.0;
            }
        }

        return score;
    }

    private static bool IsLikelyDashRelaxed(
        Rect bounds,
        double medianCharacterHeight)
    {
        if (IsLikelyDash(
                bounds,
                medianCharacterHeight))
        {
            return true;
        }

        double aspectRatio =
            bounds.Width /
            (double)Math.Max(
                1,
                bounds.Height);

        /*
         * Manche Binärbilder teilen den Bindestrich oder machen ihn etwas
         * höher. Diese lockerere Prüfung wird nur an der strukturell
         * erwarteten Bindestrichposition verwendet.
         */
        return bounds.Width >= 2 &&
               bounds.Height >= 2 &&
               aspectRatio >= 0.85 &&
               aspectRatio <= 10.0 &&
               bounds.Height <=
               Math.Max(
                   7.0,
                   medianCharacterHeight * 0.78);
    }

    private static bool IsLikelyDash(
        Rect bounds,
        double medianCharacterHeight)
    {
        double aspectRatio =
            bounds.Width /
            (double)Math.Max(
                1,
                bounds.Height);

        return bounds.Width >= 3 &&
               bounds.Height >= 2 &&
               aspectRatio >= 1.25 &&
               aspectRatio <= 8.0 &&
               bounds.Height <=
               Math.Max(
                   5.0,
                   medianCharacterHeight * 0.62);
    }

    private static double ScoreCardNumberSequence(
        IReadOnlyList<Rect> sequence,
        int dashIndex,
        double fallbackMedianHeight)
    {
        Rect dash =
            sequence[dashIndex];

        List<Rect> characters =
            sequence
                .Where((_, index) =>
                    index != dashIndex)
                .ToList();

        double medianHeight =
            GetMedian(
                characters.Select(rectangle =>
                    rectangle.Height));

        if (medianHeight <= 0)
        {
            medianHeight =
                fallbackMedianHeight;
        }

        double score =
            100.0;

        foreach (Rect character in
                 characters)
        {
            double heightDifference =
                Math.Abs(
                    character.Height -
                    medianHeight) /
                Math.Max(
                    1.0,
                    medianHeight);

            score -=
                heightDifference * 24.0;

            double aspectRatio =
                character.Width /
                (double)Math.Max(
                    1,
                    character.Height);

            if (aspectRatio < 0.05 ||
                aspectRatio > 1.45)
            {
                score -=
                    18.0;
            }
        }

        for (int index = 1;
             index < sequence.Count;
             index++)
        {
            int gap =
                sequence[index].Left -
                sequence[index - 1].Right;

            /*
             * Sehr große Sprünge bedeuten meist, dass zwei verschiedene
             * Textbereiche zusammengefasst wurden. Kleine Überlappungen sind
             * wegen Padding und Anti-Aliasing erlaubt.
             */
            if (gap >
                medianHeight * 0.90)
            {
                score -=
                    35.0;
            }
            else if (gap <
                     -medianHeight * 0.20)
            {
                score -=
                    15.0;
            }
        }

        double dashCenterY =
            dash.Y +
            dash.Height / 2.0;

        double characterCenterY =
            characters.Average(character =>
                character.Y +
                character.Height / 2.0);

        /*
         * Der Bindestrich liegt ungefähr in der mittleren Zeichenhöhe.
         * Eine starke vertikale Abweichung spricht eher für eine Grafik.
         */
        score -=
            Math.Abs(
                dashCenterY -
                characterCenterY) /
            Math.Max(
                1.0,
                medianHeight) *
            20.0;

        return score;
    }

    private static double GetMedian(
        IEnumerable<int> values)
    {
        int[] ordered =
            values
                .OrderBy(value =>
                    value)
                .ToArray();

        if (ordered.Length == 0)
        {
            return 0;
        }

        int middle =
            ordered.Length / 2;

        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] +
               ordered[middle]) / 2.0
            : ordered[middle];
    }

    private static Rect Combine(
        Rect first,
        Rect second)
    {
        int left =
            Math.Min(
                first.Left,
                second.Left);

        int top =
            Math.Min(
                first.Top,
                second.Top);

        int right =
            Math.Max(
                first.Right,
                second.Right);

        int bottom =
            Math.Max(
                first.Bottom,
                second.Bottom);

        return new Rect(
            left,
            top,
            right - left,
            bottom - top);
    }

    private static Rect AddPadding(
        Rect bounds,
        Mat image,
        int padding)
    {
        int x =
            Math.Max(
                0,
                bounds.X -
                padding);

        int y =
            Math.Max(
                0,
                bounds.Y -
                padding);

        int right =
            Math.Min(
                image.Width,
                bounds.Right +
                padding);

        int bottom =
            Math.Min(
                image.Height,
                bounds.Bottom +
                padding);

        return new Rect(
            x,
            y,
            Math.Max(
                1,
                right - x),
            Math.Max(
                1,
                bottom - y));
    }

    private static Mat NormalizeCharacter(
        Mat character)
    {
        /*
         * CharacterMatcher verwendet dieselbe Zielgröße. Dadurch kann
         * er Live-Segmente direkt vergleichen, ohne erneut zu binarisieren.
         */
        Mat canvas =
            new(
                TargetHeight,
                TargetWidth,
                MatType.CV_8UC1,
                Scalar.Black);

        double scale =
            Math.Min(
                (TargetWidth -
                 CanvasPadding * 2.0) /
                Math.Max(
                    1,
                    character.Width),

                (TargetHeight -
                 CanvasPadding * 2.0) /
                Math.Max(
                    1,
                    character.Height));

        int resizedWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    character.Width *
                    scale));

        int resizedHeight =
            Math.Max(
                1,
                (int)Math.Round(
                    character.Height *
                    scale));

        using Mat resized =
            new();

        Cv2.Resize(
            character,
            resized,
            new Size(
                resizedWidth,
                resizedHeight),
            0,
            0,
            InterpolationFlags.Area);

        int x =
            (TargetWidth -
             resizedWidth) / 2;

        int y =
            (TargetHeight -
             resizedHeight) / 2;

        using Mat targetArea =
            new(
                canvas,
                new Rect(
                    x,
                    y,
                    resizedWidth,
                    resizedHeight));

        resized.CopyTo(
            targetArea);

        return canvas;
    }
}
