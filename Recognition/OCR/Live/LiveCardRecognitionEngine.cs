using OnePieceCardScanner.Recognition.OCR.CardNumberRecognition;
using OnePieceCardScanner.Recognition.Segmentation;
using OnePieceCardScanner.Recognition.TemplateMatching;
using OnePieceCardScanner.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OnePieceCardScanner.Recognition.OCR.Live;

public sealed class LiveCardRecognitionResult
{
    public string CardNumber { get; init; } =
        string.Empty;

    public string GreedyText { get; init; } =
        string.Empty;

    public double Confidence { get; init; }

    public double ImageScore { get; init; }

    public int SegmentCount { get; init; }

    public string SourceImagePath { get; init; } =
        string.Empty;

    public bool Success =>
        !string.IsNullOrWhiteSpace(
            CardNumber);
}

public sealed class LiveCardRecognitionEngine : IDisposable
{
    private const int TopMatchesPerSegment = 3;
    private const int MaximumRegionsPerPreparedImage = 10;
    private const int MaximumDatabaseCandidates = 25;

    private const double EarlyExitConfidence = 97.0;
    private const double MissingCharacterPenalty = 30.0;
    private const double ExtraSegmentPenalty = 18.0;

    /*
     * Normale One-Piece-Kartennummern haben meistens acht Zeichen:
     * OP06-091, EB01-021, ST14-007.
     *
     * P-030 besitzt fünf Zeichen und PRB02-013 neun Zeichen.
     * Für Geschwindigkeit wird zuerst Länge 8 getestet.
     */
    private static readonly int[] ExpectedLengths =
    {
        8,
        5,
        9
    };

    private readonly CharacterSegmenter _segmenter =
        new();

    private readonly WideSegmentSplitter _wideSegmentSplitter =
        new();

    private readonly CardNumberRegionDetector _regionDetector =
        new();

    private readonly CharacterTemplateLibrary _templateLibrary;

    private readonly CharacterMatcher _matcher;

    private readonly IReadOnlyList<string> _knownCardNumbers;

    private readonly Dictionary<int, IReadOnlyList<string>>
        _knownNumbersByLength;

    private bool _isDisposed;

    public LiveCardRecognitionEngine()
    {
        string solutionFolder =
            GetSolutionFolder();

        string templateFolder =
            Path.Combine(
                solutionFolder,
                "Data",
                "OCRTemplates");

        if (!Directory.Exists(
                templateFolder))
        {
            throw new DirectoryNotFoundException(
                $"OCR-Template-Ordner nicht gefunden:\n{templateFolder}");
        }

        _templateLibrary =
            new CharacterTemplateLibrary();

        _templateLibrary.Load(
            templateFolder);

        _matcher =
            new CharacterMatcher(
                _templateLibrary);

        var database =
            new LocalCardDatabaseService();

        database.Load();

        _knownCardNumbers =
            database
                .GetAllCards()
                .Select(card =>
                    LocalCardDatabaseService
                        .GetPrintedCardNumber(
                            card.Id)
                        .Trim()
                        .ToUpperInvariant())
                .Where(number =>
                    !string.IsNullOrWhiteSpace(
                        number))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(number =>
                    number,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        _knownNumbersByLength =
            _knownCardNumbers
                .GroupBy(number =>
                    number.Length)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        (IReadOnlyList<string>)group.ToList());
    }

    /// <summary>
    /// Erkennt die Kartennummer aus einem vollständigen Kartenbild.
    /// Diese Methode ist für den Button "Bild erkennen" gedacht.
    /// </summary>
    public LiveCardRecognitionResult Recognize(
        string cardImagePath)
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        if (!File.Exists(
                cardImagePath))
        {
            throw new FileNotFoundException(
                "Das Kartenbild wurde nicht gefunden.",
                cardImagePath);
        }

        IReadOnlyList<string> coarseCandidates =
            CardRegionExtractor
                .ExtractCardNumberCandidates(
                    cardImagePath);

        LiveCandidate? best =
            null;

        foreach (string candidatePath in
                 coarseCandidates)
        {
            string preparedPath =
                CardImagePreprocessor.Prepare(
                    candidatePath);

            LiveCandidate candidate =
                RecognizePreparedImageInternal(
                    preparedPath);

            if (best == null ||
                IsBetterCandidate(
                    candidate,
                    best))
            {
                best =
                    candidate;
                                if (best.IsUsable &&
                    best.DatabaseScore >= 98.5 &&
                    best.LengthDifference == 0)
                {
                    break;
                }
            }

            if (best.IsUsable &&
                best.DatabaseScore >=
                    EarlyExitConfidence)
            {
                break;
            }
        }

        return ToPublicResult(
            best);
    }

    /// <summary>
    /// Erkennt die Kartennummer aus einem bereits vorverarbeiteten
    /// Kartennummer-Ausschnitt.
    /// </summary>
    public LiveCardRecognitionResult RecognizePreparedImage(
        string preparedImagePath)
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        if (!File.Exists(
                preparedImagePath))
        {
            throw new FileNotFoundException(
                "Das vorbereitete OCR-Bild wurde nicht gefunden.",
                preparedImagePath);
        }

        return ToPublicResult(
            RecognizePreparedImageInternal(
                preparedImagePath));
    }

    private LiveCandidate RecognizePreparedImageInternal(
        string preparedImagePath)
    {
        LiveCandidate? best =
            null;

        foreach (int expectedLength in
                 ExpectedLengths)
        {
            LiveCandidate candidate =
                RecognizeBestRegion(
                    preparedImagePath,
                    expectedLength);

            if (best == null ||
                IsBetterCandidate(
                    candidate,
                    best))
            {
                best =
                    candidate;
            }

            /*
             * Länge 8 wird zuerst geprüft. Bei einem sehr sicheren Treffer
             * werden die selteneren Längen 5 und 9 nicht mehr getestet.
             */
            if (best.IsUsable &&
                best.DatabaseScore >=
                    EarlyExitConfidence &&
                best.LengthDifference == 0)
            {
                break;
            }
        }

        return best ??
               new LiveCandidate
               {
                   SourceImagePath =
                       preparedImagePath,

                   IsUsable =
                       false
               };
    }

    private LiveCandidate RecognizeBestRegion(
        string sourceImagePath,
        int expectedLength)
    {
        string temporaryFolder =
            Path.Combine(
                GetSolutionFolder(),
                "Data",
                "OCRRuntime",
                "LiveTempRegions",
                Guid.NewGuid().ToString("N"));

        try
        {
            IReadOnlyList<string> allRegionPaths =
                _regionDetector.CreateCandidateImages(
                    sourceImagePath,
                    expectedLength,
                    temporaryFolder);

            List<string> rankedRegionPaths =
                RankRegionPaths(
                    allRegionPaths,
                    expectedLength)
                .Take(
                    MaximumRegionsPerPreparedImage)
                .ToList();

            LiveCandidate? best =
                null;

            foreach (string regionPath in
                     rankedRegionPaths)
            {
                LiveCandidate candidate =
                    RecognizeRegion(
                        regionPath,
                        sourceImagePath);

                if (best == null ||
                    IsBetterCandidate(
                        candidate,
                        best))
                {
                    best =
                        candidate;
                }

                if (best.IsUsable &&
                    best.DatabaseScore >=
                        EarlyExitConfidence &&
                    best.LengthDifference == 0)
                {
                    break;
                }
            }

            return best ??
                   new LiveCandidate
                   {
                       SourceImagePath =
                           sourceImagePath,

                       IsUsable =
                           false
                   };
        }
        finally
        {
            TryDeleteDirectory(
                temporaryFolder);
        }
    }

    private LiveCandidate RecognizeRegion(
        string regionPath,
        string sourceImagePath)
    {
        IReadOnlyList<CharacterSegment> initialSegments =
            _segmenter.Segment(
                regionPath);

        IReadOnlyList<CharacterSegment> segments =
            _wideSegmentSplitter.SplitWideSegments(
                initialSegments);

        try
        {
            if (segments.Count == 0)
            {
                return new LiveCandidate
                {
                    SourceImagePath =
                        sourceImagePath,

                    IsUsable =
                        false
                };
            }

            /*
             * Sehr unplausible Regionen werden verworfen, bevor der teure
             * Matcher und der Datenbankvergleich ausgeführt werden.
             */
            if (segments.Count < 4 ||
                segments.Count > 14)
            {
                return new LiveCandidate
                {
                    SourceImagePath =
                        sourceImagePath,

                    IsUsable =
                        false,

                    SegmentCount =
                        segments.Count
                };
            }

            var alternatives =
                new List<IReadOnlyList<CharacterMatch>>(
                    segments.Count);

            var greedyBuilder =
                new StringBuilder();

            double imageScoreSum =
                0;

            foreach (CharacterSegment segment in
                     segments)
            {
                IReadOnlyList<CharacterMatch> matches =
                    _matcher.Match(
                        segment.Image,
                        top: TopMatchesPerSegment);

                if (matches.Count == 0)
                {
                    return new LiveCandidate
                    {
                        SourceImagePath =
                            sourceImagePath,

                        IsUsable =
                            false,

                        SegmentCount =
                            segments.Count
                    };
                }

                alternatives.Add(
                    matches);

                greedyBuilder.Append(
                    matches[0].Character);

                imageScoreSum +=
                    matches[0].Score;
            }

            string greedyText =
                greedyBuilder.ToString();

            List<string> candidatePool =
                SelectLikelyDatabaseCandidates(
                    greedyText,
                    alternatives);

            DatabaseCandidateScore? bestDatabaseCandidate =
                candidatePool
                    .Select(cardNumber =>
                        ScoreDatabaseCandidate(
                            cardNumber,
                            greedyText,
                            alternatives))
                    .OrderByDescending(item =>
                        item.Score)
                    .FirstOrDefault();

            if (bestDatabaseCandidate == null)
            {
                return new LiveCandidate
                {
                    SourceImagePath =
                        sourceImagePath,

                    IsUsable =
                        false,

                    GreedyText =
                        greedyText,

                    AverageScore =
                        imageScoreSum /
                        segments.Count,

                    SegmentCount =
                        segments.Count
                };
            }

            return new LiveCandidate
            {
                SourceImagePath =
                    sourceImagePath,

                IsUsable =
                    true,

                Text =
                    bestDatabaseCandidate.CardNumber,

                GreedyText =
                    greedyText,

                AverageScore =
                    imageScoreSum /
                    segments.Count,

                DatabaseScore =
                    bestDatabaseCandidate.Score,

                SegmentCount =
                    segments.Count,

                LengthDifference =
                    Math.Abs(
                        segments.Count -
                        bestDatabaseCandidate.CardNumber.Length)
            };
        }
        finally
        {
            /*
             * WideSegmentSplitter gibt die finale Liste zurück.
             * Deshalb werden nur diese finalen Bilder hier freigegeben.
             */
            foreach (CharacterSegment segment in
                     segments)
            {
                segment.Image.Dispose();
            }
        }
    }

    private List<string> SelectLikelyDatabaseCandidates(
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        int segmentCount =
            alternatives.Count;

        IEnumerable<string> lengthCandidates =
            GetNumbersNearLength(
                segmentCount);

        string possiblePrefix =
            ExtractPossiblePrefix(
                greedyText);

        if (!string.IsNullOrWhiteSpace(
                possiblePrefix))
        {
            List<string> matchingPrefix =
                lengthCandidates
                    .Where(number =>
                        number.StartsWith(
                            possiblePrefix,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (matchingPrefix.Count >= 3)
            {
                lengthCandidates =
                    matchingPrefix;
            }
        }

        return lengthCandidates
            .Select(number =>
                new
                {
                    Number =
                        number,

                    Score =
                        CalculateQuickCandidateScore(
                            number,
                            greedyText,
                            alternatives)
                })
            .OrderByDescending(item =>
                item.Score)
            .Take(
                MaximumDatabaseCandidates)
            .Select(item =>
                item.Number)
            .ToList();
    }

    private IEnumerable<string> GetNumbersNearLength(
        int segmentCount)
    {
        /*
         * Regionen enthalten häufig zusätzliche Zeichen links/rechts.
         * Daher werden Nummern berücksichtigt, die bis zu fünf Zeichen
         * kürzer und höchstens zwei Zeichen länger als die Segmentliste sind.
         */
        int minimumLength =
            Math.Max(
                3,
                segmentCount - 5);

        int maximumLength =
            segmentCount + 2;

        for (int length = minimumLength;
             length <= maximumLength;
             length++)
        {
            if (_knownNumbersByLength.TryGetValue(
                    length,
                    out IReadOnlyList<string>? numbers))
            {
                foreach (string number in
                         numbers)
                {
                    yield return number;
                }
            }
        }
    }

    private static string ExtractPossiblePrefix(
        string greedyText)
    {
        string letters =
            new string(
                greedyText
                    .TakeWhile(character =>
                        char.IsLetter(
                            character))
                    .Take(3)
                    .ToArray());

        return letters.Length >= 1
            ? letters
            : string.Empty;
    }

    private static double CalculateQuickCandidateScore(
        string candidate,
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        double topMatchScore =
            CalculateBestAlignedTopMatchScore(
                candidate,
                alternatives);

        double windowSimilarity =
            CalculateBestWindowSimilarity(
                greedyText,
                candidate);

        double prefixBonus =
            CalculatePrefixBonus(
                greedyText,
                candidate);

        return topMatchScore *
               0.60 +
               windowSimilarity *
               0.35 +
               prefixBonus;
    }

    private static double CalculateBestAlignedTopMatchScore(
        string candidate,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        double best =
            double.NegativeInfinity;

        for (int offset = -3;
             offset <= 3;
             offset++)
        {
            double score =
                0;

            for (int characterIndex = 0;
                 characterIndex < candidate.Length;
                 characterIndex++)
            {
                int segmentIndex =
                    characterIndex +
                    offset;

                if (segmentIndex < 0 ||
                    segmentIndex >= alternatives.Count)
                {
                    score -=
                        MissingCharacterPenalty;

                    continue;
                }

                char expectedCharacter =
                    candidate[
                        characterIndex];

                CharacterMatch? exact =
                    alternatives[segmentIndex]
                        .FirstOrDefault(match =>
                            match.Character ==
                            expectedCharacter);

                score +=
                    exact?.Score ??
                    18.0;
            }

            int usedStart =
                Math.Max(
                    0,
                    offset);

            int usedEnd =
                Math.Min(
                    alternatives.Count,
                    candidate.Length +
                    offset);

            int usedSegments =
                Math.Max(
                    0,
                    usedEnd -
                    usedStart);

            int extraSegments =
                alternatives.Count -
                usedSegments;

            score -=
                extraSegments *
                ExtraSegmentPenalty;

            double normalized =
                score /
                Math.Max(
                    1,
                    candidate.Length);

            best =
                Math.Max(
                    best,
                    normalized);
        }

        return Math.Clamp(
            best,
            0,
            100);
    }

    private static DatabaseCandidateScore ScoreDatabaseCandidate(
        string cardNumber,
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        double alignmentScore =
            CalculateDynamicAlignmentScore(
                cardNumber,
                alternatives);

        double windowSimilarity =
            CalculateBestWindowSimilarity(
                greedyText,
                cardNumber);

        double prefixBonus =
            CalculatePrefixBonus(
                greedyText,
                cardNumber);

        double exactSubstringBonus =
            greedyText.Contains(
                cardNumber,
                StringComparison.OrdinalIgnoreCase)
                ? 8.0
                : 0.0;

        double finalScore =
            alignmentScore *
            0.62 +
            windowSimilarity *
            0.33 +
            prefixBonus +
            exactSubstringBonus;

        return new DatabaseCandidateScore
        {
            CardNumber =
                cardNumber,

            Score =
                Math.Clamp(
                    finalScore,
                    0,
                    100)
        };
    }

    private static double CalculateDynamicAlignmentScore(
        string cardNumber,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        int segmentCount =
            alternatives.Count;

        int characterCount =
            cardNumber.Length;

        double[,] scores =
            new double[
                segmentCount + 1,
                characterCount + 1];

        for (int segmentIndex = 0;
             segmentIndex <= segmentCount;
             segmentIndex++)
        {
            for (int characterIndex = 0;
                 characterIndex <= characterCount;
                 characterIndex++)
            {
                scores[
                    segmentIndex,
                    characterIndex] =
                    double.NegativeInfinity;
            }
        }

        scores[0, 0] =
            0;

        for (int segmentIndex = 0;
             segmentIndex <= segmentCount;
             segmentIndex++)
        {
            for (int characterIndex = 0;
                 characterIndex <= characterCount;
                 characterIndex++)
            {
                double current =
                    scores[
                        segmentIndex,
                        characterIndex];

                if (double.IsNegativeInfinity(
                        current))
                {
                    continue;
                }

                if (segmentIndex <
                    segmentCount)
                {
                    SetMaximum(
                        scores,
                        segmentIndex + 1,
                        characterIndex,
                        current -
                        ExtraSegmentPenalty);
                }

                if (characterIndex <
                    characterCount)
                {
                    SetMaximum(
                        scores,
                        segmentIndex,
                        characterIndex + 1,
                        current -
                        MissingCharacterPenalty);
                }

                if (segmentIndex <
                        segmentCount &&
                    characterIndex <
                        characterCount)
                {
                    double characterScore =
                        GetCharacterScore(
                            alternatives[
                                segmentIndex],
                            cardNumber[
                                characterIndex]);

                    SetMaximum(
                        scores,
                        segmentIndex + 1,
                        characterIndex + 1,
                        current +
                        characterScore);
                }
            }
        }

        double rawScore =
            scores[
                segmentCount,
                characterCount];

        return Math.Clamp(
            rawScore /
            Math.Max(
                1,
                characterCount *
                100.0) *
            100.0,
            0,
            100);
    }

    private static void SetMaximum(
        double[,] values,
        int row,
        int column,
        double candidate)
    {
        if (candidate >
            values[row, column])
        {
            values[row, column] =
                candidate;
        }
    }

    private static double GetCharacterScore(
        IReadOnlyList<CharacterMatch> matches,
        char expectedCharacter)
    {
        CharacterMatch? exact =
            matches.FirstOrDefault(match =>
                match.Character ==
                expectedCharacter);

        return exact?.Score ??
               18.0;
    }

    private static double CalculatePrefixBonus(
        string source,
        string candidate)
    {
        string sourceLetters =
            new string(
                source
                    .Where(character =>
                        char.IsLetter(
                            character))
                    .Take(3)
                    .ToArray());

        string candidateLetters =
            new string(
                candidate
                    .TakeWhile(character =>
                        char.IsLetter(
                            character))
                    .Take(3)
                    .ToArray());

        if (sourceLetters.Length == 0 ||
            candidateLetters.Length == 0)
        {
            return 0;
        }

        if (candidateLetters.StartsWith(
                sourceLetters,
                StringComparison.OrdinalIgnoreCase) ||
            sourceLetters.StartsWith(
                candidateLetters,
                StringComparison.OrdinalIgnoreCase))
        {
            return 5.0;
        }

        return 0;
    }

    private static double CalculateBestWindowSimilarity(
        string source,
        string candidate)
    {
        if (string.IsNullOrWhiteSpace(
                source) ||
            string.IsNullOrWhiteSpace(
                candidate))
        {
            return 0;
        }

        if (source.Contains(
                candidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        int minimumWindowLength =
            Math.Max(
                1,
                candidate.Length - 2);

        int maximumWindowLength =
            Math.Min(
                source.Length,
                candidate.Length + 3);

        double best =
            0;

        for (int windowLength = minimumWindowLength;
             windowLength <= maximumWindowLength;
             windowLength++)
        {
            for (int start = 0;
                 start + windowLength <= source.Length;
                 start++)
            {
                string window =
                    source.Substring(
                        start,
                        windowLength);

                int distance =
                    CalculateLevenshteinDistance(
                        window,
                        candidate);

                int maximumLength =
                    Math.Max(
                        window.Length,
                        candidate.Length);

                double similarity =
                    maximumLength == 0
                        ? 100
                        : 100.0 *
                          (1.0 -
                           distance /
                           (double)maximumLength);

                best =
                    Math.Max(
                        best,
                        similarity);
            }
        }

        return Math.Clamp(
            best,
            0,
            100);
    }

    private static int CalculateLevenshteinDistance(
        string left,
        string right)
    {
        int[,] distances =
            new int[
                left.Length + 1,
                right.Length + 1];

        for (int leftIndex = 0;
             leftIndex <= left.Length;
             leftIndex++)
        {
            distances[leftIndex, 0] =
                leftIndex;
        }

        for (int rightIndex = 0;
             rightIndex <= right.Length;
             rightIndex++)
        {
            distances[0, rightIndex] =
                rightIndex;
        }

        for (int leftIndex = 1;
             leftIndex <= left.Length;
             leftIndex++)
        {
            for (int rightIndex = 1;
                 rightIndex <= right.Length;
                 rightIndex++)
            {
                int substitutionCost =
                    char.ToUpperInvariant(
                        left[
                            leftIndex - 1]) ==
                    char.ToUpperInvariant(
                        right[
                            rightIndex - 1])
                        ? 0
                        : 1;

                distances[
                    leftIndex,
                    rightIndex] =
                    Math.Min(
                        Math.Min(
                            distances[
                                leftIndex - 1,
                                rightIndex] + 1,
                            distances[
                                leftIndex,
                                rightIndex - 1] + 1),
                        distances[
                            leftIndex - 1,
                            rightIndex - 1] +
                        substitutionCost);
            }
        }

        return distances[
            left.Length,
            right.Length];
    }

    private static IEnumerable<string> RankRegionPaths(
        IEnumerable<string> regionPaths,
        int expectedLength)
    {
        return regionPaths
            .Select(path =>
                new
                {
                    Path =
                        path,

                    Quality =
                        CalculateRegionQuality(
                            path,
                            expectedLength)
                })
            .OrderByDescending(item =>
                item.Quality)
            .Select(item =>
                item.Path);
    }

    private static double CalculateRegionQuality(
        string regionPath,
        int expectedLength)
    {
        try
        {
            using Mat image =
                Cv2.ImRead(
                    regionPath,
                    ImreadModes.Grayscale);

            if (image.Empty())
            {
                return double.NegativeInfinity;
            }

            using Mat binary =
                new();

            Cv2.Threshold(
                image,
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
                    image.Width *
                    image.Height);

            double density =
                whitePixels /
                (double)totalPixels;

            if (density > 0.5)
            {
                density =
                    1.0 -
                    density;
            }

            double aspectRatio =
                image.Width /
                (double)Math.Max(
                    1,
                    image.Height);

            double expectedAspect =
                Math.Max(
                    2.0,
                    expectedLength *
                    0.55);

            double aspectPenalty =
                Math.Abs(
                    aspectRatio -
                    expectedAspect);

            double densityScore =
                100.0 -
                Math.Abs(
                    density -
                    0.18) *
                250.0;

            double sizeScore =
                Math.Min(
                    100.0,
                    image.Width *
                    image.Height /
                    20.0);

            return densityScore *
                   0.55 +
                   sizeScore *
                   0.20 -
                   aspectPenalty *
                   8.0;
        }
        catch
        {
            return double.NegativeInfinity;
        }
    }

    private static bool IsBetterCandidate(
        LiveCandidate candidate,
        LiveCandidate currentBest)
    {
        if (candidate.IsUsable !=
            currentBest.IsUsable)
        {
            return candidate.IsUsable;
        }

        if (Math.Abs(
                candidate.DatabaseScore -
                currentBest.DatabaseScore) >
            0.01)
        {
            return candidate.DatabaseScore >
                   currentBest.DatabaseScore;
        }

        if (candidate.LengthDifference !=
            currentBest.LengthDifference)
        {
            return candidate.LengthDifference <
                   currentBest.LengthDifference;
        }

        return candidate.AverageScore >
               currentBest.AverageScore;
    }

    private static LiveCardRecognitionResult ToPublicResult(
        LiveCandidate? candidate)
    {
        if (candidate == null ||
            !candidate.IsUsable)
        {
            return new LiveCardRecognitionResult();
        }

        return new LiveCardRecognitionResult
        {
            CardNumber =
                candidate.Text,

            GreedyText =
                candidate.GreedyText,

            Confidence =
                candidate.DatabaseScore,

            ImageScore =
                candidate.AverageScore,

            SegmentCount =
                candidate.SegmentCount,

            SourceImagePath =
                candidate.SourceImagePath
        };
    }

    private static string GetSolutionFolder()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                @"..\..\..\.."));
    }

    private static void TryDeleteDirectory(
        string directoryPath)
    {
        try
        {
            if (Directory.Exists(
                    directoryPath))
            {
                Directory.Delete(
                    directoryPath,
                    recursive: true);
            }
        }
        catch
        {
            /*
             * Temporäre Live-OCR-Dateien dürfen die Erkennung
             * nicht abbrechen.
             */
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _matcher.Dispose();

        foreach (CharacterTemplate template in
                 _templateLibrary.GetAllTemplates())
        {
            template.Image.Dispose();
        }

        _isDisposed =
            true;
    }

    private sealed class LiveCandidate
    {
        public string SourceImagePath { get; init; } =
            string.Empty;

        public bool IsUsable { get; init; }

        public string Text { get; init; } =
            string.Empty;

        public string GreedyText { get; init; } =
            string.Empty;

        public double AverageScore { get; init; }

        public double DatabaseScore { get; init; }

        public int SegmentCount { get; init; }

        public int LengthDifference { get; init; }
    }

    private sealed class DatabaseCandidateScore
    {
        public string CardNumber { get; init; } =
            string.Empty;

        public double Score { get; init; }
    }
}
