using OnePieceCardScanner.Recognition.OCR.CardNumberRecognition;
using OnePieceCardScanner.Recognition.Segmentation;
using OnePieceCardScanner.Recognition.TemplateMatching;
using OnePieceCardScanner.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OnePieceCardScanner.Recognition.OCR;

public sealed class CardNumberRecognitionResult
{
    public string CardNumber { get; init; } =
        string.Empty;

    public string GreedyText { get; init; } =
        string.Empty;

    public double Confidence { get; init; }

    public double ImageScore { get; init; }

    public int SegmentCount { get; init; }

    public string SourceRegionPath { get; init; } =
        string.Empty;

    public bool Success =>
        !string.IsNullOrWhiteSpace(
            CardNumber);
}

public sealed class CardNumberRecognitionEngine : IDisposable
{
    private const int TopMatchesPerSegment = 8;
    private const int MaximumDatabaseCandidates = 80;
    private const double MissingCharacterPenalty = 28.0;
    private const double ExtraSegmentPenalty = 16.0;

    private static readonly int[] ExpectedLengths =
    {
        8, // OP01-001, EB01-001, ST01-001
        5, // P-030
        9  // PRB01-001
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

    private bool _isDisposed;

    public CardNumberRecognitionEngine()
    {
        string solutionFolder =
            GetSolutionFolder();

        string templateFolder =
            Path.Combine(
                solutionFolder,
                "Data",
                "OCRTemplates");

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
    }

    public CardNumberRecognitionResult RecognizeCard(
        string detectedCardPath)
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        if (!File.Exists(
                detectedCardPath))
        {
            throw new FileNotFoundException(
                "Das Kartenbild wurde nicht gefunden.",
                detectedCardPath);
        }

        string temporaryFolder =
            Path.Combine(
                GetSolutionFolder(),
                "Data",
                "OCRRuntime",
                "FixedNumberRegions",
                Guid.NewGuid().ToString("N"));

        try
        {
            IReadOnlyList<string> fixedCandidates =
                FixedCardNumberRegionExtractor
                    .ExtractCandidates(
                        detectedCardPath,
                        temporaryFolder);

            CandidateRecognition? best =
                null;

            foreach (string candidatePath in
                     fixedCandidates)
            {
                string preparedPath =
                    CardImagePreprocessor.Prepare(
                        candidatePath);

                CandidateRecognition candidate =
                    RecognizeRegion(
                        preparedPath,
                        candidatePath);

                if (best == null ||
                    IsBetterCandidate(
                        candidate,
                        best))
                {
                    best =
                        candidate;
                }

                /*
                 * Bei einem sicheren Treffer wird keine weitere
                 * Ausschnittvariante mehr geprüft.
                 */
                if (IsConclusiveCandidate(
                        best))
                {
                    break;
                }
            }

            /*
             * Nur bei einem unsicheren festen Ausschnitt wird die
             * langsame, bisherige Regionssuche als Fallback verwendet.
             * Gute Scannerbilder benötigen diesen Weg normalerweise nicht.
             */
            if (best == null ||
                !IsConclusiveCandidate(
                    best))
            {
                CardNumberRecognitionResult fallback =
                    RecognizeCardSlowFallback(
                        detectedCardPath);

                if (fallback.Success)
                {
                    CandidateRecognition fallbackCandidate =
                        new()
                        {
                            SourceImagePath =
                                fallback.SourceRegionPath,

                            IsUsable =
                                true,

                            Text =
                                fallback.CardNumber,

                            GreedyText =
                                fallback.GreedyText,

                            AverageScore =
                                fallback.ImageScore,

                            DatabaseScore =
                                fallback.Confidence,

                            SegmentCount =
                                fallback.SegmentCount,

                            LengthDifference =
                                Math.Abs(
                                    fallback.SegmentCount -
                                    fallback.CardNumber.Length)
                        };

                    if (best == null ||
                        IsBetterCandidate(
                            fallbackCandidate,
                            best))
                    {
                        best =
                            fallbackCandidate;
                    }
                }
            }

            if (best == null ||
                !best.IsUsable)
            {
                return new CardNumberRecognitionResult();
            }

            return new CardNumberRecognitionResult
            {
                CardNumber =
                    best.Text,

                GreedyText =
                    best.GreedyText,

                Confidence =
                    best.DatabaseScore,

                ImageScore =
                    best.AverageScore,

                SegmentCount =
                    best.SegmentCount,

                SourceRegionPath =
                    best.SourceImagePath
            };
        }
        finally
        {
            TryDeleteDirectory(
                temporaryFolder);
        }
    }

    private CardNumberRecognitionResult RecognizeCardSlowFallback(
        string detectedCardPath)
    {
        IReadOnlyList<string> coarseCandidates =
            CardRegionExtractor
                .ExtractCardNumberCandidates(
                    detectedCardPath);

        CandidateRecognition? best =
            null;

        /*
         * Der Fallback prüft höchstens zwei grobe Kandidaten.
         * Dadurch bleibt auch der schwierige Pfad begrenzt.
         */
        foreach (string candidatePath in
                 coarseCandidates.Take(6))
        {
            string preparedPath =
                CardImagePreprocessor.Prepare(
                    candidatePath);

            CardNumberRecognitionResult preparedResult =
                RecognizePreparedImage(
                    preparedPath);

            if (!preparedResult.Success)
            {
                continue;
            }

            CandidateRecognition candidate =
                new()
                {
                    SourceImagePath =
                        preparedResult.SourceRegionPath,

                    IsUsable =
                        true,

                    Text =
                        preparedResult.CardNumber,

                    GreedyText =
                        preparedResult.GreedyText,

                    AverageScore =
                        preparedResult.ImageScore,

                    DatabaseScore =
                        preparedResult.Confidence,

                    SegmentCount =
                        preparedResult.SegmentCount,

                    LengthDifference =
                        Math.Abs(
                            preparedResult.SegmentCount -
                            preparedResult.CardNumber.Length)
                };

            if (best == null ||
                IsBetterCandidate(
                    candidate,
                    best))
            {
                best =
                    candidate;
            }

        }

        if (best == null ||
            !best.IsUsable)
        {
            return new CardNumberRecognitionResult();
        }

        return new CardNumberRecognitionResult
        {
            CardNumber =
                best.Text,

            GreedyText =
                best.GreedyText,

            Confidence =
                best.DatabaseScore,

            ImageScore =
                best.AverageScore,

            SegmentCount =
                best.SegmentCount,

            SourceRegionPath =
                best.SourceImagePath
        };
    }

    public CardNumberRecognitionResult RecognizePreparedImage(
        string preparedImagePath)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        CandidateRecognition best = RecognizeBestRegion(preparedImagePath);
        if (!best.IsUsable)
        {
            return new CardNumberRecognitionResult();
        }

        return ToPublicResult(best);
    }

    private CardNumberRecognitionResult ToPublicResult(
        CandidateRecognition candidate)
    {
        return new CardNumberRecognitionResult
        {
            CardNumber = candidate.Text,
            GreedyText = candidate.GreedyText,
            Confidence = candidate.DatabaseScore,
            ImageScore = candidate.AverageScore,
            SegmentCount = candidate.SegmentCount,
            SourceRegionPath = candidate.SourceImagePath
        };
    }

    private CandidateRecognition RecognizeBestRegion(
        string sourceImagePath)
    {
        string temporaryFolder = Path.Combine(
            GetSolutionFolder(),
            "Data",
            "OCRRuntime",
            "TempRegions",
            Guid.NewGuid().ToString("N"));

        try
        {
            IReadOnlyList<string> regionPaths =
                _regionDetector.CreateCandidateImagesForUnknownLength(
                    sourceImagePath,
                    temporaryFolder,
                    maximumCandidates: 36);

            CandidateRecognition? best = null;
            foreach (string regionPath in regionPaths)
            {
                CandidateRecognition candidate =
                    RecognizeRegion(regionPath, sourceImagePath);

                if (best == null || IsBetterCandidate(candidate, best))
                {
                    best = candidate;
                }

                if (IsConclusiveCandidate(
                        best))
                {
                    break;
                }
            }

            return best ?? new CandidateRecognition
            {
                SourceImagePath = sourceImagePath,
                IsUsable = false
            };
        }
        finally
        {
            TryDeleteDirectory(temporaryFolder);
        }
    }

    private CandidateRecognition RecognizeRegion(
        string regionPath,
        string sourceImagePath)
    {
        IReadOnlyList<SegmentationHypothesis> hypotheses =
            _segmenter.GenerateHypotheses(
                regionPath,
                maximumHypotheses: 32);

        CandidateRecognition? best = null;
        try
        {
            foreach (SegmentationHypothesis hypothesis in hypotheses)
            {
                IReadOnlyList<CharacterSegment> originalSegments =
                    CloneSegments(hypothesis.Segments);

                CandidateRecognition originalCandidate = RecognizeSegments(
                    originalSegments,
                    sourceImagePath,
                    $"{hypothesis.SourceName}:original",
                    hypothesis.GeometryScore);

                if (best == null || IsBetterCandidate(originalCandidate, best))
                {
                    best = originalCandidate;
                }

                IReadOnlyList<CharacterSegment> splitInput =
                    CloneSegments(hypothesis.Segments);

                IReadOnlyList<CharacterSegment> splitSegments =
                    _wideSegmentSplitter.SplitWideSegments(splitInput);

                CandidateRecognition candidate = RecognizeSegments(
                    splitSegments,
                    sourceImagePath,
                    $"{hypothesis.SourceName}:wide-split",
                    hypothesis.GeometryScore);

                if (best == null || IsBetterCandidate(candidate, best))
                {
                    best = candidate;
                }
            }
        }
        finally
        {
            foreach (SegmentationHypothesis hypothesis in hypotheses)
            {
                hypothesis.Dispose();
            }
        }

        return best ?? new CandidateRecognition
        {
            SourceImagePath = sourceImagePath,
            IsUsable = false
        };
    }

    private static bool IsConclusiveCandidate(
        CandidateRecognition candidate)
    {
        return candidate.IsUsable &&
               candidate.LengthDifference == 0 &&
               candidate.DatabaseScore >= 99.7 &&
               candidate.AverageScore >= 82.0 &&
               candidate.GeometryScore >= 82.0 &&
               string.Equals(
                   candidate.GreedyText,
                   candidate.Text,
                   StringComparison.OrdinalIgnoreCase);
    }

    private CandidateRecognition RecognizeSegments(
        IReadOnlyList<CharacterSegment> segments,
        string sourceImagePath,
        string hypothesisName,
        double geometryScore)
    {
        try
        {
            if (segments.Count == 0)
            {
                return Unusable(sourceImagePath, hypothesisName, geometryScore, 0);
            }

            var alternatives = new List<IReadOnlyList<CharacterMatch>>();
            var greedyBuilder = new StringBuilder();
            double scoreSum = 0;

            foreach (CharacterSegment segment in segments)
            {
                IReadOnlyList<CharacterMatch> matches =
                    _matcher.Match(segment.Image, top: TopMatchesPerSegment);

                if (matches.Count == 0)
                {
                    return Unusable(
                        sourceImagePath,
                        hypothesisName,
                        geometryScore,
                        segments.Count);
                }

                alternatives.Add(matches);
                greedyBuilder.Append(matches[0].Character);
                scoreSum += matches[0].Score;
            }

            string greedyText = greedyBuilder.ToString();
            double averageScore = scoreSum / Math.Max(1, segments.Count);

            DatabaseCandidateScore? databaseCandidate =
                SelectLikelyDatabaseCandidates(greedyText, alternatives)
                    .Select(cardNumber => ScoreDatabaseCandidate(
                        cardNumber,
                        greedyText,
                        alternatives))
                    .OrderByDescending(item => item.Score)
                    .FirstOrDefault();

            if (databaseCandidate == null)
            {
                return new CandidateRecognition
                {
                    SourceImagePath = sourceImagePath,
                    IsUsable = false,
                    GreedyText = greedyText,
                    AverageScore = averageScore,
                    SegmentCount = segments.Count,
                    HypothesisName = hypothesisName,
                    GeometryScore = geometryScore
                };
            }

            int lengthDifference = Math.Abs(
                segments.Count - databaseCandidate.CardNumber.Length);

            return new CandidateRecognition
            {
                SourceImagePath = sourceImagePath,
                IsUsable = true,
                Text = databaseCandidate.CardNumber,
                GreedyText = greedyText,
                AverageScore = averageScore,
                DatabaseScore = databaseCandidate.Score,
                GlobalScore = CalculateGlobalHypothesisScore(
                    databaseCandidate.Score,
                    averageScore,
                    geometryScore,
                    lengthDifference,
                    greedyText,
                    databaseCandidate.CardNumber),
                SegmentCount = segments.Count,
                LengthDifference = lengthDifference,
                HypothesisName = hypothesisName,
                GeometryScore = geometryScore
            };
        }
        finally
        {
            foreach (CharacterSegment segment in segments)
            {
                segment.Image.Dispose();
            }
        }
    }

    private static CandidateRecognition Unusable(
        string sourceImagePath,
        string hypothesisName,
        double geometryScore,
        int segmentCount)
    {
        return new CandidateRecognition
        {
            SourceImagePath = sourceImagePath,
            IsUsable = false,
            HypothesisName = hypothesisName,
            GeometryScore = geometryScore,
            SegmentCount = segmentCount
        };
    }

    private static IReadOnlyList<CharacterSegment> CloneSegments(
        IReadOnlyList<CharacterSegment> segments)
    {
        return segments.Select(segment => new CharacterSegment
        {
            Position = segment.Position,
            Bounds = segment.Bounds,
            Image = segment.Image.Clone()
        }).ToList();
    }

    private static double CalculateGlobalHypothesisScore(
        double databaseScore,
        double imageScore,
        double geometryScore,
        int lengthDifference,
        string greedyText,
        string cardNumber)
    {
        double formatScore = IsStructurallyPlausibleCardNumber(cardNumber)
            ? 100.0
            : 0.0;

        double exactGreedyBonus = string.Equals(
            greedyText,
            cardNumber,
            StringComparison.OrdinalIgnoreCase)
                ? 4.0
                : 0.0;

        double score =
            databaseScore * 0.58 +
            imageScore * 0.18 +
            Math.Clamp(geometryScore, 0, 100) * 0.16 +
            formatScore * 0.08 +
            exactGreedyBonus -
            lengthDifference * 7.5;

        return Math.Clamp(score, 0, 104);
    }

    private static bool IsStructurallyPlausibleCardNumber(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Length switch
        {
            5 => value[1] == '-',
            8 => value[4] == '-',
            9 => value[5] == '-',
            _ => false
        };
    }

    private List<string> SelectLikelyDatabaseCandidates(
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        int segmentCount =
            alternatives.Count;

        return _knownCardNumbers
            .Where(number =>
                number.Length >=
                    Math.Max(
                        3,
                        segmentCount - 5) &&
                number.Length <=
                    segmentCount + 2)
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

    private static double CalculateQuickCandidateScore(
        string candidate,
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        double best =
            double.NegativeInfinity;

        for (int offset = -2;
             offset <= 2;
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

                char expected =
                    candidate[
                        characterIndex];

                CharacterMatch? match =
                    alternatives[segmentIndex]
                        .FirstOrDefault(item =>
                            item.Character ==
                            expected);

                score +=
                    match?.Score ??
                    25;
            }

            int alignedStart =
                Math.Max(
                    0,
                    offset);

            int alignedEnd =
                Math.Min(
                    alternatives.Count,
                    candidate.Length +
                    offset);

            int extraSegments =
                alternatives.Count -
                Math.Max(
                    0,
                    alignedEnd -
                    alignedStart);

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

        return best +
               CalculateBestWindowSimilarity(
                   greedyText,
                   candidate) *
               0.75;
    }

    private static DatabaseCandidateScore ScoreDatabaseCandidate(
        string cardNumber,
        string greedyText,
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
                    scores[
                        segmentIndex + 1,
                        characterIndex] =
                        Math.Max(
                            scores[
                                segmentIndex + 1,
                                characterIndex],
                            current -
                            ExtraSegmentPenalty);
                }

                if (characterIndex <
                    characterCount)
                {
                    scores[
                        segmentIndex,
                        characterIndex + 1] =
                        Math.Max(
                            scores[
                                segmentIndex,
                                characterIndex + 1],
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

                    scores[
                        segmentIndex + 1,
                        characterIndex + 1] =
                        Math.Max(
                            scores[
                                segmentIndex + 1,
                                characterIndex + 1],
                            current +
                            characterScore);
                }
            }
        }

        double alignmentScore =
            Math.Clamp(
                scores[
                    segmentCount,
                    characterCount] /
                Math.Max(
                    1,
                    characterCount *
                    100.0) *
                100.0,
                0,
                100);

        double windowSimilarity =
            CalculateBestWindowSimilarity(
                greedyText,
                cardNumber);

        double substringBonus =
            greedyText.Contains(
                cardNumber,
                StringComparison.OrdinalIgnoreCase)
                ? 8.0
                : 0.0;

        return new DatabaseCandidateScore
        {
            CardNumber =
                cardNumber,

            Score =
                Math.Clamp(
                    alignmentScore *
                    0.65 +
                    windowSimilarity *
                    0.35 +
                    substringBonus,
                    0,
                    100)
        };
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

        int minimumLength =
            Math.Max(
                1,
                candidate.Length - 2);

        int maximumLength =
            Math.Min(
                source.Length,
                candidate.Length + 2);

        double best =
            0;

        for (int windowLength = minimumLength;
             windowLength <= maximumLength;
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

                int maximumComparedLength =
                    Math.Max(
                        window.Length,
                        candidate.Length);

                double similarity =
                    maximumComparedLength == 0
                        ? 100
                        : 100.0 *
                          (1.0 -
                           distance /
                           (double)maximumComparedLength);

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

    private static bool IsBetterCandidate(
        CandidateRecognition candidate,
        CandidateRecognition currentBest)
    {
        if (candidate.IsUsable != currentBest.IsUsable)
        {
            return candidate.IsUsable;
        }

        if (!candidate.IsUsable)
        {
            return candidate.AverageScore > currentBest.AverageScore;
        }

        double candidateGlobal = candidate.GlobalScore > 0
            ? candidate.GlobalScore
            : CalculateGlobalHypothesisScore(
                candidate.DatabaseScore,
                candidate.AverageScore,
                candidate.GeometryScore,
                candidate.LengthDifference,
                candidate.GreedyText,
                candidate.Text);

        double bestGlobal = currentBest.GlobalScore > 0
            ? currentBest.GlobalScore
            : CalculateGlobalHypothesisScore(
                currentBest.DatabaseScore,
                currentBest.AverageScore,
                currentBest.GeometryScore,
                currentBest.LengthDifference,
                currentBest.GreedyText,
                currentBest.Text);

        if (Math.Abs(candidateGlobal - bestGlobal) > 0.35)
        {
            return candidateGlobal > bestGlobal;
        }

        if (candidate.LengthDifference != currentBest.LengthDifference)
        {
            return candidate.LengthDifference < currentBest.LengthDifference;
        }

        if (Math.Abs(candidate.DatabaseScore - currentBest.DatabaseScore) > 0.25)
        {
            return candidate.DatabaseScore > currentBest.DatabaseScore;
        }

        if (Math.Abs(candidate.GeometryScore - currentBest.GeometryScore) > 0.5)
        {
            return candidate.GeometryScore > currentBest.GeometryScore;
        }

        return candidate.AverageScore > currentBest.AverageScore;
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

    private sealed class CandidateRecognition
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

        public double GlobalScore { get; init; }

        public double GeometryScore { get; init; }

        public string HypothesisName { get; init; } =
            string.Empty;

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
