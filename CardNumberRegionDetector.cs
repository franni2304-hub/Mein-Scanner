using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnePieceCardScanner.Recognition.OCR.CardNumberRecognition;

public sealed class CardNumberRegionDetector
{
    private const int MaximumCandidates = 15;

    public IReadOnlyList<string> CreateCandidateImages(
        string sourceImagePath,
        int expectedCharacterCount,
        string temporaryFolder)
    {
        return CreateCandidateImages(
            sourceImagePath,
            expectedCharacterCount,
            temporaryFolder,
            MaximumCandidates);
    }

    public IReadOnlyList<string> CreateCandidateImages(
        string sourceImagePath,
        int expectedCharacterCount,
        string temporaryFolder,
        int maximumCandidates)
    {
        maximumCandidates =
            Math.Clamp(
                maximumCandidates,
                1,
                MaximumCandidates);
        if (!File.Exists(
                sourceImagePath))
        {
            throw new FileNotFoundException(
                "Das Eingabebild wurde nicht gefunden.",
                sourceImagePath);
        }

        Directory.CreateDirectory(
            temporaryFolder);

        using Mat source =
            Cv2.ImRead(
                sourceImagePath,
                ImreadModes.Grayscale);

        if (source.Empty())
        {
            return [];
        }

        using Mat binary =
            CreateBinaryImage(
                source);

        Point[][] contours =
            Cv2.FindContoursAsArray(
                binary,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

        List<Rect> components =
            contours
                .Select(Cv2.BoundingRect)
                .Where(rectangle =>
                    IsPossibleTextComponent(
                        rectangle,
                        source.Size()))
                .OrderBy(rectangle =>
                    rectangle.X)
                .ToList();

        List<List<Rect>> rows =
            GroupIntoRows(
                components);

        var candidateRectangles =
            new List<Rect>();

        foreach (List<Rect> row in
                 rows)
        {
            row.Sort(
                (left, right) =>
                    left.X.CompareTo(
                        right.X));

            AddRowCandidates(
                row,
                expectedCharacterCount,
                source.Size(),
                candidateRectangles);
        }

        /*
         * Rückfallkandidaten nur erzeugen, wenn die Kontursuche
         * kaum brauchbare Bereiche gefunden hat. Das spart bei
         * normalen Bildern unnötige OCR-Durchläufe.
         */
        if (candidateRectangles.Count < 5)
        {
            int lowerStart =
                (int)Math.Round(
                    source.Height * 0.45);

            candidateRectangles.Add(
                new Rect(
                    0,
                    Math.Clamp(
                        lowerStart,
                        0,
                        Math.Max(
                            0,
                            source.Height - 1)),
                    source.Width,
                    Math.Max(
                        1,
                        source.Height -
                        lowerStart)));

            candidateRectangles.Add(
                new Rect(
                    0,
                    0,
                    source.Width,
                    source.Height));
        }

        List<Rect> distinctRectangles =
            Deduplicate(
                candidateRectangles)
            .Select(rectangle =>
                new CandidateRegion
                {
                    Bounds =
                        rectangle,

                    Score =
                        ScoreCandidateRegion(
                            rectangle,
                            source.Size(),
                            expectedCharacterCount)
                })
            .OrderByDescending(candidate =>
                candidate.Score)
            .ThenBy(candidate =>
                candidate.Bounds.Width *
                candidate.Bounds.Height)
            .Take(
                maximumCandidates)
            .Select(candidate =>
                candidate.Bounds)
            .ToList();

        string sourceName =
            Path.GetFileNameWithoutExtension(
                sourceImagePath);

        var outputPaths =
            new List<string>();

        for (int index = 0;
             index < distinctRectangles.Count;
             index++)
        {
            Rect bounds =
                distinctRectangles[index];

            using Mat cropped =
                new Mat(
                    source,
                    bounds);

            string outputPath =
                Path.Combine(
                    temporaryFolder,
                    $"{MakeSafeFileName(sourceName)}_" +
                    $"region_{index:000}.png");

            Cv2.ImWrite(
                outputPath,
                cropped);

            outputPaths.Add(
                outputPath);
        }

        return outputPaths;
    }

    /// <summary>
    /// Erzeugt Regionskandidaten, ohne die Länge der Kartennummer vorher
    /// festzulegen. Kandidaten für Promo-, Standard- und PRB-Formate werden
    /// gemeinsam erzeugt, bildinhaltlich dedupliziert und erst anschließend
    /// begrenzt.
    /// </summary>
    public IReadOnlyList<string> CreateCandidateImagesForUnknownLength(
        string sourceImagePath,
        string temporaryFolder,
        int maximumCandidates = 24)
    {
        maximumCandidates =
            Math.Clamp(
                maximumCandidates,
                1,
                40);

        Directory.CreateDirectory(
            temporaryFolder);

        int[] expectedLengths =
        {
            8,
            9,
            5
        };

        var allPaths =
            new List<string>();

        for (int formatIndex = 0;
             formatIndex < expectedLengths.Length;
             formatIndex++)
        {
            int expectedLength =
                expectedLengths[formatIndex];

            string formatFolder =
                Path.Combine(
                    temporaryFolder,
                    $"length_{expectedLength}");

            int perFormatLimit =
                expectedLength switch
                {
                    8 => 15,
                    9 => 12,
                    5 => 9,
                    _ => 10
                };

            allPaths.AddRange(
                CreateCandidateImages(
                    sourceImagePath,
                    expectedLength,
                    formatFolder,
                    perFormatLimit));
        }

        var accepted =
            new List<string>();

        var signatures =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (string path in
                 allPaths)
        {
            string signature =
                CalculateImageSignature(
                    path);

            if (!signatures.Add(
                    signature))
            {
                continue;
            }

            accepted.Add(
                path);

            if (accepted.Count >=
                maximumCandidates)
            {
                break;
            }
        }

        return accepted;
    }

    private static string CalculateImageSignature(
        string imagePath)
    {
        using Mat image =
            Cv2.ImRead(
                imagePath,
                ImreadModes.Grayscale);

        if (image.Empty())
        {
            return imagePath;
        }

        using Mat resized =
            new();

        Cv2.Resize(
            image,
            resized,
            new Size(32, 12),
            0,
            0,
            InterpolationFlags.Area);

        Scalar mean =
            Cv2.Mean(
                resized);

        var bits =
            new char[resized.Rows * resized.Cols];

        int position =
            0;

        for (int y = 0;
             y < resized.Rows;
             y++)
        {
            for (int x = 0;
                 x < resized.Cols;
                 x++)
            {
                bits[position++] =
                    resized.At<byte>(y, x) >= mean.Val0
                        ? '1'
                        : '0';
            }
        }

        return
            $"{image.Width}x{image.Height}:" +
            new string(bits);
    }

    private static Mat CreateBinaryImage(
        Mat source)
    {
        using Mat blurred =
            new();

        Cv2.GaussianBlur(
            source,
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
                binary.Width *
                binary.Height);

        if (whitePixels >
            totalPixels / 2)
        {
            Cv2.BitwiseNot(
                binary,
                binary);
        }

        return binary;
    }

    private static bool IsPossibleTextComponent(
        Rect bounds,
        Size imageSize)
    {
        if (bounds.Width < 2 ||
            bounds.Height < 4)
        {
            return false;
        }

        int area =
            bounds.Width *
            bounds.Height;

        if (area < 12)
        {
            return false;
        }

        if (bounds.Width >
            imageSize.Width * 0.50 ||
            bounds.Height >
            imageSize.Height * 0.95)
        {
            return false;
        }

        double ratio =
            bounds.Width /
            (double)Math.Max(
                1,
                bounds.Height);

        bool normalCharacter =
            ratio >= 0.05 &&
            ratio <= 2.2;

        bool possibleDash =
            bounds.Height >= 2 &&
            ratio > 1.5 &&
            ratio <= 10;

        return normalCharacter ||
               possibleDash;
    }

    private static List<List<Rect>> GroupIntoRows(
        IReadOnlyList<Rect> components)
    {
        var rows =
            new List<List<Rect>>();

        foreach (Rect component in
                 components
                     .OrderBy(rectangle =>
                         rectangle.Y))
        {
            List<Rect>? bestRow =
                null;

            double bestOverlap =
                0;

            foreach (List<Rect> row in
                     rows)
            {
                Rect rowBounds =
                    CombineAll(
                        row);

                int overlap =
                    Math.Min(
                        rowBounds.Bottom,
                        component.Bottom) -
                    Math.Max(
                        rowBounds.Top,
                        component.Top);

                double overlapRatio =
                    overlap /
                    (double)Math.Max(
                        1,
                        Math.Min(
                            rowBounds.Height,
                            component.Height));

                if (overlapRatio >
                    bestOverlap)
                {
                    bestOverlap =
                        overlapRatio;

                    bestRow =
                        row;
                }
            }

            if (bestRow != null &&
                bestOverlap >= 0.35)
            {
                bestRow.Add(
                    component);
            }
            else
            {
                rows.Add(
                    [component]);
            }
        }

        return rows
            .Where(row =>
                row.Count >= 3)
            .ToList();
    }

    private static void AddRowCandidates(
        IReadOnlyList<Rect> row,
        int expectedCharacterCount,
        Size imageSize,
        ICollection<Rect> output)
    {
        if (row.Count == 0)
        {
            return;
        }

        int minimumWindow =
            Math.Max(
                3,
                expectedCharacterCount - 1);

        int maximumWindow =
            Math.Min(
                row.Count,
                expectedCharacterCount + 2);

        for (int windowSize = minimumWindow;
             windowSize <= maximumWindow;
             windowSize++)
        {
            for (int start = 0;
                 start + windowSize <= row.Count;
                 start++)
            {
                IReadOnlyList<Rect> window =
                    row
                        .Skip(start)
                        .Take(windowSize)
                        .ToList();

                Rect combined =
                    CombineAll(
                        window);

                if (!LooksLikeCardNumberRegion(
                        combined,
                        window,
                        imageSize))
                {
                    continue;
                }

                double medianHeight =
                    GetMedian(
                        window.Select(component =>
                            component.Height));

                int leftPadding =
                    Math.Max(
                        4,
                        (int)Math.Round(
                            medianHeight * 0.22));

                /*
                 * Rechts wird bewusst mehr Platz gelassen. In den
                 * Benchmark-Fehlern war die letzte Ziffer häufig bereits im
                 * Regionsbild abgeschnitten, obwohl ihre Kontur noch teilweise
                 * erkannt wurde.
                 */
                int rightPadding =
                    Math.Max(
                        10,
                        (int)Math.Round(
                            medianHeight * 0.62));

                int verticalPadding =
                    Math.Max(
                        4,
                        (int)Math.Round(
                            medianHeight * 0.18));

                /*
                 * Kompakter Kandidat: hilfreich bei ST/OP11-Karten, wenn
                 * direkt rechts neben der Nummer ein Rarity-Symbol beginnt.
                 */
                output.Add(
                    AddPadding(
                        combined,
                        imageSize,
                        leftPadding:
                            Math.Max(
                                3,
                                (int)Math.Round(
                                    medianHeight * 0.16)),
                        rightPadding:
                            Math.Max(
                                5,
                                (int)Math.Round(
                                    medianHeight * 0.30)),
                        topPadding:
                            verticalPadding,
                        bottomPadding:
                            verticalPadding));

                /*
                 * Großzügiger Kandidat: bewahrt die bisherige Absicherung
                 * gegen abgeschnittene letzte Ziffern.
                 */
                output.Add(
                    AddPadding(
                        combined,
                        imageSize,
                        leftPadding,
                        rightPadding,
                        verticalPadding,
                        verticalPadding));
            }
        }

        /*
         * Eine komplette Zeile ist nur dann ein sinnvoller Kandidat, wenn sie
         * nicht deutlich mehr Komponenten als die erwartete Kartennummer
         * enthält. Dadurch werden Kostenwerte, SP/SR-Symbole und normaler
         * Kartentext nicht mehr als besonders großer Kandidat bevorzugt.
         */
        if (row.Count <=
            expectedCharacterCount + 2)
        {
            Rect completeRow =
                CombineAll(
                    row);

            double medianHeight =
                GetMedian(
                    row.Select(component =>
                        component.Height));

            output.Add(
                AddPadding(
                    completeRow,
                    imageSize,
                    leftPadding:
                        Math.Max(
                            4,
                            (int)Math.Round(
                                medianHeight * 0.22)),
                    rightPadding:
                        Math.Max(
                            10,
                            (int)Math.Round(
                                medianHeight * 0.62)),
                    topPadding:
                        Math.Max(
                            4,
                            (int)Math.Round(
                                medianHeight * 0.18)),
                    bottomPadding:
                        Math.Max(
                            4,
                            (int)Math.Round(
                                medianHeight * 0.18))));
        }
    }

    private static bool LooksLikeCardNumberRegion(
        Rect combined,
        IReadOnlyList<Rect> components,
        Size imageSize)
    {
        if (combined.Width <
            imageSize.Width * 0.08)
        {
            return false;
        }

        if (combined.Width >
            imageSize.Width * 0.98)
        {
            return false;
        }

        if (combined.Height >
            imageSize.Height * 0.85)
        {
            return false;
        }

        double medianHeight =
            components
                .Select(component =>
                    component.Height)
                .OrderBy(value =>
                    value)
                .ElementAt(
                    components.Count / 2);

        int similarHeightCount =
            components.Count(component =>
                component.Height >=
                medianHeight * 0.45 &&
                component.Height <=
                medianHeight * 1.80);

        return similarHeightCount >=
               Math.Max(
                   3,
                   components.Count / 2);
    }

    private static double ScoreCandidateRegion(
        Rect bounds,
        Size imageSize,
        int expectedCharacterCount)
    {
        double widthRatio =
            bounds.Width /
            (double)Math.Max(
                1,
                imageSize.Width);

        double heightRatio =
            bounds.Height /
            (double)Math.Max(
                1,
                imageSize.Height);

        double aspectRatio =
            bounds.Width /
            (double)Math.Max(
                1,
                bounds.Height);

        double expectedAspectRatio =
            Math.Max(
                2.0,
                expectedCharacterCount * 0.52);

        double aspectDifference =
            Math.Abs(
                aspectRatio -
                expectedAspectRatio);

        double aspectScore =
            Math.Clamp(
                100.0 -
                aspectDifference * 12.0,
                0,
                100);

        /*
         * Kartennummern sind breite, eher flache Textbereiche.
         */
        double widthScore =
            Math.Clamp(
                widthRatio * 180.0,
                0,
                100);

        double heightScore =
            heightRatio <= 0.40
                ? Math.Clamp(
                    heightRatio * 260.0,
                    0,
                    100)
                : Math.Clamp(
                    100.0 -
                    (heightRatio - 0.40) * 220.0,
                    0,
                    100);

        /*
         * Bei vollständigen Kartenbildern befindet sich die Nummer
         * normalerweise eher im unteren Bereich. Bei bereits engen
         * OCR-Ausschnitten bleibt die Gewichtung bewusst klein.
         */
        double verticalCenter =
            (bounds.Y +
             bounds.Height / 2.0) /
            Math.Max(
                1,
                imageSize.Height);

        double positionScore =
            Math.Clamp(
                verticalCenter * 100.0,
                0,
                100);

        double areaRatio =
            bounds.Width *
            bounds.Height /
            (double)Math.Max(
                1,
                imageSize.Width *
                imageSize.Height);

        double areaScore =
            Math.Clamp(
                areaRatio * 260.0,
                0,
                100);

        return aspectScore * 0.38 +
               widthScore * 0.24 +
               heightScore * 0.16 +
               positionScore * 0.12 +
               areaScore * 0.10;
    }

    private static IEnumerable<Rect> Deduplicate(
        IEnumerable<Rect> rectangles)
    {
        var accepted =
            new List<Rect>();

        foreach (Rect rectangle in
                 rectangles
                     .Where(rectangle =>
                         rectangle.Width > 0 &&
                         rectangle.Height > 0)
                     .OrderBy(rectangle =>
                         rectangle.Width *
                         rectangle.Height))
        {
            bool duplicate =
                accepted.Any(existing =>
                    IntersectionOverUnion(
                        existing,
                        rectangle) >= 0.88);

            if (!duplicate)
            {
                accepted.Add(
                    rectangle);
            }
        }

        return accepted;
    }

    private static double IntersectionOverUnion(
        Rect first,
        Rect second)
    {
        int left =
            Math.Max(
                first.Left,
                second.Left);

        int top =
            Math.Max(
                first.Top,
                second.Top);

        int right =
            Math.Min(
                first.Right,
                second.Right);

        int bottom =
            Math.Min(
                first.Bottom,
                second.Bottom);

        if (right <= left ||
            bottom <= top)
        {
            return 0;
        }

        int intersection =
            (right - left) *
            (bottom - top);

        int union =
            first.Width *
            first.Height +
            second.Width *
            second.Height -
            intersection;

        return union <= 0
            ? 0
            : intersection /
              (double)union;
    }

    private static Rect CombineAll(
        IEnumerable<Rect> rectangles)
    {
        Rect[] array =
            rectangles.ToArray();

        int left =
            array.Min(rectangle =>
                rectangle.Left);

        int top =
            array.Min(rectangle =>
                rectangle.Top);

        int right =
            array.Max(rectangle =>
                rectangle.Right);

        int bottom =
            array.Max(rectangle =>
                rectangle.Bottom);

        return new Rect(
            left,
            top,
            right - left,
            bottom - top);
    }

    private static Rect AddPadding(
        Rect bounds,
        Size imageSize,
        int horizontalPadding,
        int verticalPadding)
    {
        return AddPadding(
            bounds,
            imageSize,
            horizontalPadding,
            horizontalPadding,
            verticalPadding,
            verticalPadding);
    }

    private static Rect AddPadding(
        Rect bounds,
        Size imageSize,
        int leftPadding,
        int rightPadding,
        int topPadding,
        int bottomPadding)
    {
        int x =
            Math.Max(
                0,
                bounds.X -
                leftPadding);

        int y =
            Math.Max(
                0,
                bounds.Y -
                topPadding);

        int right =
            Math.Min(
                imageSize.Width,
                bounds.Right +
                rightPadding);

        int bottom =
            Math.Min(
                imageSize.Height,
                bounds.Bottom +
                bottomPadding);

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

    private sealed class CandidateRegion
    {
        public Rect Bounds { get; init; }

        public double Score { get; init; }
    }
}
