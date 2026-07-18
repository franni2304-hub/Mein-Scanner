using OnePieceCardScanner.Recognition.OCR.CardNumberRecognition;
using OnePieceCardScanner.Recognition.Segmentation;
using OnePieceCardScanner.Recognition.TemplateMatching;
using OnePieceCardScanner.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OnePieceCardScanner.Recognition.OCR;

public enum OcrBenchmarkMode
{
    MatcherOnly,
    EndToEnd
}

public enum BenchmarkFailureType
{
    SegmentationFailed,
    TooFewSegments,
    TooManySegments,
    PrefixMismatch,
    DigitMismatch,
    LetterMismatch,
    DatabaseWrongDecision,
    LowConfidence,
    MixedMismatch,
    Unknown
}

public sealed class OcrBenchmarkProgress
{
    public int Current { get; init; }

    public int Total { get; init; }

    public string CurrentCard { get; init; } =
        string.Empty;

    public int CorrectCards { get; init; }

    public int FailedCards { get; init; }

    public TimeSpan Elapsed { get; init; }

    public TimeSpan? EstimatedRemaining { get; init; }

    public double Percent =>
        Total <= 0
            ? 0
            : Current * 100.0 / Total;
}

public sealed class OcrBenchmarkResult
{
    public OcrBenchmarkMode Mode { get; init; }

    public string ReportPath { get; init; } =
        string.Empty;

    public int TestedCards { get; init; }

    public int CorrectCards { get; init; }

    public int FailedCards { get; init; }

    public int CorrectCharacters { get; init; }

    public int TotalCharacters { get; init; }

    public TimeSpan Duration { get; init; }

    public double CardAccuracy =>
        TestedCards <= 0
            ? 0
            : CorrectCards * 100.0 / TestedCards;

    public double CharacterAccuracy =>
        TotalCharacters <= 0
            ? 0
            : CorrectCharacters * 100.0 / TotalCharacters;
}

public sealed class OcrBenchmark
{
    private const int NormalTopMatchesPerSegment = 5;
    private const int FastTopMatchesPerSegment = 3;

    private const int NormalMaximumDatabaseCandidates = 100;
    private const int FastMaximumDatabaseCandidates = 30;

    private const int NormalMaximumRegions = 60;
    private const int FastMaximumRegions = 15;

    private const double FastEarlyExitDatabaseScore = 97.0;

    private const double MissingCharacterPenalty = 28.0;
    private const double ExtraSegmentPenalty = 16.0;

    private readonly CharacterSegmenter _segmenter =
        new();

    private readonly WideSegmentSplitter _wideSegmentSplitter =
        new();

    private readonly CardNumberRegionDetector _regionDetector =
        new();

    public Task<OcrBenchmarkResult> RunAsync(
        IProgress<OcrBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            OcrBenchmarkMode.EndToEnd,
            progress,
            cancellationToken);
    }

    public Task<OcrBenchmarkResult> RunAsync(
        int? preprocessedSampleSize,
        IProgress<OcrBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => RunEndToEndFromPublicEntry(
                preprocessedSampleSize,
                progress,
                cancellationToken),
            cancellationToken);
    }

    public Task<OcrBenchmarkResult> RunAsync(
        OcrBenchmarkMode mode,
        IProgress<OcrBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Run(
                mode,
                progress,
                cancellationToken),
            cancellationToken);
    }

    public OcrBenchmarkResult Run(
        OcrBenchmarkMode mode = OcrBenchmarkMode.EndToEnd,
        IProgress<OcrBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
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
                $"Template-Ordner nicht gefunden: {templateFolder}");
        }

        var library =
            new CharacterTemplateLibrary();

        library.Load(
            templateFolder);

        var database =
            new LocalCardDatabaseService();

        database.Load();

        List<string> knownCardNumbers =
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

        try
        {
            using var matcher =
                new CharacterMatcher(
                    library);

            return mode switch
            {
                OcrBenchmarkMode.MatcherOnly =>
                    RunMatcherOnly(
                        library,
                        matcher,
                        progress,
                        cancellationToken),

                OcrBenchmarkMode.EndToEnd =>
                    RunEndToEnd(
                        solutionFolder,
                        templateFolder,
                        matcher,
                        knownCardNumbers,
                        progress,
                        cancellationToken,
                        preprocessedSampleSize: 0),

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(mode),
                        mode,
                        "Unbekannter Benchmark-Modus.")
            };
        }
        finally
        {
            DisposeLibrary(
                library);
        }
    }

    private OcrBenchmarkResult RunEndToEndFromPublicEntry(
        int? preprocessedSampleSize,
        IProgress<OcrBenchmarkProgress>? progress,
        CancellationToken cancellationToken)
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
                $"Template-Ordner nicht gefunden: {templateFolder}");
        }

        var library =
            new CharacterTemplateLibrary();

        library.Load(
            templateFolder);

        var database =
            new LocalCardDatabaseService();

        database.Load();

        List<string> knownCardNumbers =
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

        try
        {
            using var matcher =
                new CharacterMatcher(
                    library);

            return RunEndToEnd(
                solutionFolder,
                templateFolder,
                matcher,
                knownCardNumbers,
                progress,
                cancellationToken,
                preprocessedSampleSize);
        }
        finally
        {
            DisposeLibrary(
                library);
        }
    }

    private OcrBenchmarkResult RunMatcherOnly(
        CharacterTemplateLibrary library,
        CharacterMatcher matcher,
        IProgress<OcrBenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<CharacterTemplate> templates =
            library
                .GetAllTemplates()
                .Where(template =>
                    template.Image != null &&
                    !template.Image.Empty())
                .ToList();

        var stopwatch =
            Stopwatch.StartNew();

        int correct = 0;
        int failed = 0;

        var confusionCounts =
            new Dictionary<(char Expected, char Actual), int>();

        var failedLines =
            new List<string>();

        for (int index = 0;
             index < templates.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CharacterTemplate template =
                templates[index];

            ReportProgress(
                progress,
                index,
                templates.Count,
                $"{template.Character} – " +
                $"{Path.GetFileName(template.FilePath)}",
                correct,
                failed,
                stopwatch.Elapsed);

            IReadOnlyList<CharacterMatch> matches =
                matcher.Match(
                    template.Image,
                    top: NormalTopMatchesPerSegment);

            CharacterMatch? best =
                matches.FirstOrDefault();

            if (best != null &&
                best.Character ==
                template.Character)
            {
                correct++;
            }
            else
            {
                failed++;

                char actual =
                    best?.Character ??
                    '?';

                AddConfusion(
                    confusionCounts,
                    template.Character,
                    actual);

                failedLines.Add(
                    $"{template.Character} -> {actual} | " +
                    $"{Path.GetFileName(template.FilePath)} | " +
                    $"Score {(best?.Score ?? 0):0.0} %");
            }
        }

        stopwatch.Stop();

        ReportProgress(
            progress,
            templates.Count,
            templates.Count,
            "Fertig",
            correct,
            failed,
            stopwatch.Elapsed);

        string reportPath =
            WriteReport(
                mode:
                    OcrBenchmarkMode.MatcherOnly,
                dataSource:
                    "gespeicherte OCR-Templates",
                testedCards:
                    templates.Count,
                correctCards:
                    correct,
                failedCards:
                    failed,
                correctCharacters:
                    correct,
                totalCharacters:
                    templates.Count,
                confusionCounts:
                    confusionCounts,
                segmentationFailures:
                    [],
                failedLines:
                    failedLines,
                benchmarkFailures:
                    [],
                databaseCorrectedCount:
                    0,
                databaseWorsenedCount:
                    0,
                databaseUnchangedCorrectCount:
                    0,
                databaseUnchangedWrongCount:
                    0,
                lowConfidenceCount:
                    0,
                duration:
                    stopwatch.Elapsed);

        return new OcrBenchmarkResult
        {
            Mode =
                OcrBenchmarkMode.MatcherOnly,

            ReportPath =
                reportPath,

            TestedCards =
                templates.Count,

            CorrectCards =
                correct,

            FailedCards =
                failed,

            CorrectCharacters =
                correct,

            TotalCharacters =
                templates.Count,

            Duration =
                stopwatch.Elapsed
        };
    }

    private OcrBenchmarkResult RunEndToEnd(
        string solutionFolder,
        string templateFolder,
        CharacterMatcher matcher,
        IReadOnlyList<string> knownCardNumbers,
        IProgress<OcrBenchmarkProgress>? progress,
        CancellationToken cancellationToken,
        int? preprocessedSampleSize = 0)
    {
        string previewFolder =
            Path.Combine(
                solutionFolder,
                "Data",
                "OCRTemplatePreview",
                "Preprocessed");

        if (!Directory.Exists(
                previewFolder))
        {
            throw new DirectoryNotFoundException(
                $"Preview-Ordner nicht gefunden: {previewFolder}");
        }

        bool usePreprocessedImages =
            preprocessedSampleSize != 0;

        List<BenchmarkCard> cards =
            usePreprocessedImages
                ? SelectPreprocessedSample(
                    LoadAllPreprocessedImages(
                        previewFolder),
                    preprocessedSampleSize)
                : LoadBenchmarkCards(
                    previewFolder,
                    templateFolder);

        var stopwatch =
            Stopwatch.StartNew();

        string failureRunFolder =
            Path.Combine(
                solutionFolder,
                "Data",
                "OCRBenchmark",
                "BenchmarkFailures",
                DateTime.Now.ToString(
                    "yyyy-MM-dd_HH-mm-ss"));

        int failureArtifactIndex = 0;

        int testedCards = 0;
        int correctCards = 0;
        int failedCards = 0;
        int correctCharacters = 0;
        int totalCharacters = 0;

        var confusionCounts =
            new Dictionary<(char Expected, char Actual), int>();

        var failedLines =
            new List<string>();

        var segmentationFailures =
            new List<string>();

        var benchmarkFailures =
            new List<BenchmarkFailure>();

        int databaseCorrectedCount = 0;
        int databaseWorsenedCount = 0;
        int databaseUnchangedCorrectCount = 0;
        int databaseUnchangedWrongCount = 0;
        int lowConfidenceCount = 0;

        for (int cardIndex = 0;
             cardIndex < cards.Count;
             cardIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BenchmarkCard card =
                cards[cardIndex];

            ReportProgress(
                progress,
                cardIndex,
                cards.Count,
                card.ExpectedText,
                correctCards,
                failedCards,
                stopwatch.Elapsed);

            testedCards++;
            totalCharacters +=
                card.ExpectedText.Length;

            CandidateRecognition? best =
                null;

            foreach (string imagePath in
                     card.ImagePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CandidateRecognition candidate =
                    RecognizeBestRegion(
                        imagePath,
                        card.ExpectedText.Length,
                        matcher,
                        knownCardNumbers,
                        cancellationToken,
                        fastMode: usePreprocessedImages);

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
                failedCards++;

                segmentationFailures.Add(
                    $"{card.DisplayId,-20} " +
                    $"erwartet {card.ExpectedText,-12} " +
                    $"beste Segmentanzahl {best?.SegmentCount ?? 0}");

                BenchmarkFailure failure =
                    CreateBenchmarkFailure(
                        card,
                        best);

                benchmarkFailures.Add(
                    failure);

                SaveFailureArtifacts(
                    failureRunFolder,
                    ++failureArtifactIndex,
                    failure,
                    best);

                continue;
            }

            bool greedyCorrect =
                string.Equals(
                    card.ExpectedText,
                    best.GreedyText,
                    StringComparison.Ordinal);

            bool databaseCorrect =
                string.Equals(
                    card.ExpectedText,
                    best.Text,
                    StringComparison.Ordinal);

            if (!greedyCorrect && databaseCorrect)
            {
                databaseCorrectedCount++;
            }
            else if (greedyCorrect && !databaseCorrect)
            {
                databaseWorsenedCount++;
            }
            else if (greedyCorrect)
            {
                databaseUnchangedCorrectCount++;
            }
            else
            {
                databaseUnchangedWrongCount++;
            }

            if (best.ConfidenceMargin < 2.0)
            {
                lowConfidenceCount++;
            }

            CountCharacterResults(
                card.ExpectedText,
                best.Text,
                confusionCounts,
                ref correctCharacters);

            if (string.Equals(
                    card.ExpectedText,
                    best.Text,
                    StringComparison.Ordinal))
            {
                correctCards++;
            }
            else
            {
                failedCards++;

                BenchmarkFailure failure =
                    CreateBenchmarkFailure(
                        card,
                        best);

                benchmarkFailures.Add(
                    failure);

                SaveFailureArtifacts(
                    failureRunFolder,
                    ++failureArtifactIndex,
                    failure,
                    best);

                failedLines.Add(
                    $"{card.DisplayId,-20} " +
                    $"erwartet {card.ExpectedText,-12} " +
                    $"erkannt {best.Text,-14} " +
                    $"DB-Score {best.DatabaseScore:0.0} % | " +
                    $"Bild-Score {best.AverageScore:0.0} % | " +
                    $"Greedy {best.GreedyText,-12} | " +
                    $"Segmente {best.SegmentCount} | " +
                    $"{Path.GetFileName(best.SourceImagePath)}");
            }
        }

        stopwatch.Stop();

        ReportProgress(
            progress,
            cards.Count,
            cards.Count,
            "Fertig",
            correctCards,
            failedCards,
            stopwatch.Elapsed);

        string dataSource =
            cards.Count == 0
                ? "keine Testbilder"
                : cards[0].SourceDescription;

        string reportPath =
            WriteReport(
                mode:
                    OcrBenchmarkMode.EndToEnd,
                dataSource:
                    dataSource +
                    (usePreprocessedImages
                        ? $" | Auswahl: {GetPreprocessedSelectionDescription(preprocessedSampleSize, cards.Count)}" +
                          " | Schnellmodus: Top-3, max. 15 Regionen, 30 Datenbankkandidaten, Early Exit"
                        : " | Datenbankgestützte Top-5-Erkennung + Teilfensterbewertung"),
                testedCards:
                    testedCards,
                correctCards:
                    correctCards,
                failedCards:
                    failedCards,
                correctCharacters:
                    correctCharacters,
                totalCharacters:
                    totalCharacters,
                confusionCounts:
                    confusionCounts,
                segmentationFailures:
                    segmentationFailures,
                failedLines:
                    failedLines,
                benchmarkFailures:
                    benchmarkFailures,
                databaseCorrectedCount:
                    databaseCorrectedCount,
                databaseWorsenedCount:
                    databaseWorsenedCount,
                databaseUnchangedCorrectCount:
                    databaseUnchangedCorrectCount,
                databaseUnchangedWrongCount:
                    databaseUnchangedWrongCount,
                lowConfidenceCount:
                    lowConfidenceCount,
                duration:
                    stopwatch.Elapsed);

        return new OcrBenchmarkResult
        {
            Mode =
                OcrBenchmarkMode.EndToEnd,

            ReportPath =
                reportPath,

            TestedCards =
                testedCards,

            CorrectCards =
                correctCards,

            FailedCards =
                failedCards,

            CorrectCharacters =
                correctCharacters,

            TotalCharacters =
                totalCharacters,

            Duration =
                stopwatch.Elapsed
        };
    }

    private CandidateRecognition RecognizeBestRegion(
        string sourceImagePath,
        int expectedLength,
        CharacterMatcher matcher,
        IReadOnlyList<string> knownCardNumbers,
        CancellationToken cancellationToken,
        bool fastMode)
    {
        string temporaryFolder =
            Path.Combine(
                GetSolutionFolder(),
                "Data",
                "OCRBenchmark",
                "TempRegions",
                Guid.NewGuid().ToString("N"));

        try
        {
            int maximumRegions =
                fastMode
                    ? FastMaximumRegions
                    : NormalMaximumRegions;

            IReadOnlyList<string> regionPaths =
                _regionDetector.CreateCandidateImages(
                    sourceImagePath,
                    expectedLength,
                    temporaryFolder,
                    maximumRegions);

            CandidateRecognition? best =
                null;

            foreach (string regionPath in
                     regionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CandidateRecognition candidate =
                    RecognizeRegion(
                        regionPath,
                        sourceImagePath,
                        matcher,
                        knownCardNumbers,
                        fastMode);

                if (best == null ||
                    IsBetterCandidate(
                        candidate,
                        best))
                {
                    best =
                        candidate;
                }

                if (fastMode &&
                    best.IsUsable &&
                    best.DatabaseScore >=
                        FastEarlyExitDatabaseScore &&
                    best.LengthDifference == 0)
                {
                    break;
                }
            }

            return best ??
                   new CandidateRecognition
                   {
                       SourceImagePath =
                           sourceImagePath,

                       IsUsable =
                           false,

                       SegmentCount =
                           0,

                       LengthDifference =
                           expectedLength
                   };
        }
        finally
        {
            TryDeleteDirectory(
                temporaryFolder);
        }
    }

    private CandidateRecognition RecognizeRegion(
        string regionPath,
        string sourceImagePath,
        CharacterMatcher matcher,
        IReadOnlyList<string> knownCardNumbers,
        bool fastMode)
    {
        IReadOnlyList<CharacterSegment> initialSegments =
            _segmenter.Segment(
                regionPath);

        IReadOnlyList<CharacterSegment> segments =
            _wideSegmentSplitter.SplitWideSegments(
                initialSegments);

        byte[] regionImagePng =
            ReadFileBytesSafely(
                regionPath);

        byte[] binaryImagePng =
            CreateBinaryPreviewPng(
                regionPath);

        IReadOnlyList<byte[]> segmentImagesPng =
            EncodeSegmentImages(
                segments);

        try
        {
            if (segments.Count == 0)
            {
                return new CandidateRecognition
                {
                    SourceImagePath =
                        sourceImagePath,

                    RegionImagePng =
                        regionImagePng,

                    BinaryImagePng =
                        binaryImagePng,

                    SegmentImagesPng =
                        segmentImagesPng,

                    IsUsable =
                        false,

                    SegmentCount =
                        0
                };
            }

            var alternatives =
                new List<IReadOnlyList<CharacterMatch>>();

            var greedyBuilder =
                new StringBuilder();

            double greedyScoreSum =
                0;

            int topMatches =
                fastMode
                    ? FastTopMatchesPerSegment
                    : NormalTopMatchesPerSegment;

            foreach (CharacterSegment segment in
                     segments)
            {
                IReadOnlyList<CharacterMatch> matches =
                    matcher.Match(
                        segment.Image,
                        top: topMatches);

                if (matches.Count == 0)
                {
                    return new CandidateRecognition
                    {
                        SourceImagePath =
                            sourceImagePath,

                        RegionImagePng =
                        regionImagePng,

                        BinaryImagePng =
                        binaryImagePng,

                        SegmentImagesPng =
                        segmentImagesPng,

                        IsUsable =
                            false,

                        SegmentCount =
                            segments.Count
                    };
                }

                alternatives.Add(
                    matches);

                CharacterMatch bestMatch =
                    matches[0];

                greedyBuilder.Append(
                    bestMatch.Character);

                greedyScoreSum +=
                    bestMatch.Score;
            }

            string greedyText =
                greedyBuilder.ToString();

            List<string> candidatePool =
                SelectLikelyDatabaseCandidates(
                    greedyText,
                    alternatives,
                    knownCardNumbers,
                    fastMode);

            List<DatabaseCandidateScore> topDatabaseCandidates =
                candidatePool
                    .Select(cardNumber =>
                        ScoreDatabaseCandidate(
                            cardNumber,
                            greedyText,
                            alternatives))
                    .OrderByDescending(score =>
                        score.Score)
                    .ThenBy(score =>
                        score.CardNumber,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();

            DatabaseCandidateScore? bestDatabaseCandidate =
                topDatabaseCandidates.FirstOrDefault();

            if (bestDatabaseCandidate == null)
            {
                return new CandidateRecognition
                {
                    SourceImagePath =
                        sourceImagePath,

                    RegionImagePng =
                        regionImagePng,

                    BinaryImagePng =
                        binaryImagePng,

                    SegmentImagesPng =
                        segmentImagesPng,

                    IsUsable =
                        true,

                    Text =
                        greedyText,

                    GreedyText =
                        greedyText,

                    AverageScore =
                        greedyScoreSum /
                        segments.Count,

                    DatabaseScore =
                        0,

                    SegmentCount =
                        segments.Count,

                    LengthDifference =
                        0,

                    TopCandidates =
                        [],

                    ConfidenceMargin =
                        0,

                    OcrConfidence =
                        Math.Clamp(
                            greedyScoreSum / segments.Count,
                            0,
                            100),

                    DatabaseConfidence =
                        0,

                    OverallConfidence =
                        Math.Clamp(
                            greedyScoreSum / segments.Count * 0.6,
                            0,
                            100)
                };
            }

            return new CandidateRecognition
            {
                SourceImagePath =
                    sourceImagePath,

                RegionImagePng =
                        regionImagePng,

                BinaryImagePng =
                        binaryImagePng,

                SegmentImagesPng =
                        segmentImagesPng,

                IsUsable =
                    true,

                Text =
                    bestDatabaseCandidate.CardNumber,

                GreedyText =
                    greedyText,

                AverageScore =
                    greedyScoreSum /
                    segments.Count,

                DatabaseScore =
                    bestDatabaseCandidate.Score,

                SegmentCount =
                    segments.Count,

                LengthDifference =
                    Math.Abs(
                        segments.Count -
                        bestDatabaseCandidate.CardNumber.Length),

                TopCandidates =
                    topDatabaseCandidates,

                ConfidenceMargin =
                    CalculateConfidenceMargin(
                        topDatabaseCandidates),

                OcrConfidence =
                    Math.Clamp(
                        greedyScoreSum / segments.Count,
                        0,
                        100),

                DatabaseConfidence =
                    CalculateDatabaseConfidence(
                        topDatabaseCandidates),

                OverallConfidence =
                    CalculateOverallConfidence(
                        greedyScoreSum / segments.Count,
                        topDatabaseCandidates)
            };
        }
        finally
        {
            foreach (CharacterSegment segment in
                     segments)
            {
                segment.Image.Dispose();
            }
        }
    }

    private static List<string> SelectLikelyDatabaseCandidates(
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives,
        IReadOnlyList<string> knownCardNumbers,
        bool fastMode)
    {
        int segmentCount =
            alternatives.Count;

        int maximumCandidates =
            fastMode
                ? FastMaximumDatabaseCandidates
                : NormalMaximumDatabaseCandidates;

        return knownCardNumbers
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

                    QuickScore =
                        CalculateQuickCandidateScore(
                            number,
                            greedyText,
                            alternatives)
                })
            .OrderByDescending(item =>
                item.QuickScore)
            .Take(
                maximumCandidates)
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

            int compared =
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

                compared++;

                char expectedCharacter =
                    candidate[characterIndex];

                CharacterMatch? match =
                    alternatives[segmentIndex]
                        .FirstOrDefault(item =>
                            item.Character ==
                            expectedCharacter);

                if (match != null)
                {
                    score +=
                        match.Score;
                }
                else if (segmentIndex <
                         greedyText.Length &&
                         greedyText[segmentIndex] ==
                         expectedCharacter)
                {
                    score +=
                        65;
                }
                else
                {
                    score +=
                        25;
                }
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

            int usedSegments =
                Math.Max(
                    0,
                    alignedEnd -
                    alignedStart);

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

            if (compared > 0 &&
                normalized > best)
            {
                best =
                    normalized;
            }
        }

        double textSimilarity =
            CalculateBestWindowSimilarity(
                greedyText,
                candidate);

        return best +
               textSimilarity * 0.75;
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
                    double skipSegmentScore =
                        current -
                        ExtraSegmentPenalty;

                    if (skipSegmentScore >
                        scores[
                            segmentIndex + 1,
                            characterIndex])
                    {
                        scores[
                            segmentIndex + 1,
                            characterIndex] =
                            skipSegmentScore;
                    }
                }

                if (characterIndex <
                    characterCount)
                {
                    double skipCharacterScore =
                        current -
                        MissingCharacterPenalty;

                    if (skipCharacterScore >
                        scores[
                            segmentIndex,
                            characterIndex + 1])
                    {
                        scores[
                            segmentIndex,
                            characterIndex + 1] =
                            skipCharacterScore;
                    }
                }

                if (segmentIndex <
                        segmentCount &&
                    characterIndex <
                        characterCount)
                {
                    char expectedCharacter =
                        cardNumber[
                            characterIndex];

                    double matchScore =
                        GetCharacterScore(
                            alternatives[
                                segmentIndex],
                            expectedCharacter);

                    double alignedScore =
                        current +
                        matchScore;

                    if (alignedScore >
                        scores[
                            segmentIndex + 1,
                            characterIndex + 1])
                    {
                        scores[
                            segmentIndex + 1,
                            characterIndex + 1] =
                            alignedScore;
                    }
                }
            }
        }

        double rawScore =
            scores[
                segmentCount,
                characterCount];

        double maximumScore =
            Math.Max(
                1,
                characterCount *
                100.0);

        double alignmentScore =
            Math.Clamp(
                rawScore /
                maximumScore *
                100.0,
                0,
                100);

        double windowSimilarity =
            CalculateBestWindowSimilarity(
                greedyText,
                cardNumber);

        double exactSubstringBonus =
            greedyText.Contains(
                cardNumber,
                StringComparison.OrdinalIgnoreCase)
                ? 8.0
                : 0.0;

        double normalizedScore =
            Math.Clamp(
                alignmentScore * 0.65 +
                windowSimilarity * 0.35 +
                exactSubstringBonus,
                0,
                100);

        return new DatabaseCandidateScore
        {
            CardNumber =
                cardNumber,

            Score =
                normalizedScore
        };
    }

    private static double CalculateConfidenceMargin(
        IReadOnlyList<DatabaseCandidateScore> candidates)
    {
        if (candidates.Count < 2)
        {
            return candidates.Count == 1
                ? candidates[0].Score
                : 0;
        }

        return Math.Max(
            0,
            candidates[0].Score - candidates[1].Score);
    }

    private static double CalculateDatabaseConfidence(
        IReadOnlyList<DatabaseCandidateScore> candidates)
    {
        if (candidates.Count == 0)
        {
            return 0;
        }

        double margin =
            CalculateConfidenceMargin(
                candidates);

        /*
         * Der beste Score allein ist keine echte Sicherheit.
         * Deshalb fließen Gewinner-Score und Abstand zu Platz 2
         * getrennt in die Diagnose-Confidence ein.
         */
        return Math.Clamp(
            candidates[0].Score * 0.75 +
            Math.Min(100, margin * 10.0) * 0.25,
            0,
            100);
    }

    private static double CalculateOverallConfidence(
        double averageOcrScore,
        IReadOnlyList<DatabaseCandidateScore> candidates)
    {
        double ocrConfidence =
            Math.Clamp(
                averageOcrScore,
                0,
                100);

        double databaseConfidence =
            CalculateDatabaseConfidence(
                candidates);

        return Math.Clamp(
            ocrConfidence * 0.45 +
            databaseConfidence * 0.55,
            0,
            100);
    }

    private static double CalculateBestWindowSimilarity(
        string source,
        string candidate)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(candidate))
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

                if (similarity > best)
                {
                    best =
                        similarity;
                }
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
                        left[leftIndex - 1]) ==
                    char.ToUpperInvariant(
                        right[rightIndex - 1])
                        ? 0
                        : 1;

                distances[leftIndex, rightIndex] =
                    Math.Min(
                        Math.Min(
                            distances[leftIndex - 1, rightIndex] + 1,
                            distances[leftIndex, rightIndex - 1] + 1),
                        distances[leftIndex - 1, rightIndex - 1] +
                        substitutionCost);
            }
        }

        return distances[
            left.Length,
            right.Length];
    }

    private static double GetCharacterScore(
        IReadOnlyList<CharacterMatch> matches,
        char expectedCharacter)
    {
        CharacterMatch? exact =
            matches.FirstOrDefault(match =>
                match.Character ==
                expectedCharacter);

        if (exact != null)
        {
            return exact.Score;
        }

        /*
         * Das Zeichen war nicht in den Top-Treffern.
         * Eine geringe Restwahrscheinlichkeit bleibt bestehen,
         * damit ein einzelnes schwaches Segment nicht sofort
         * die komplette Kartennummer ausschließt.
         */
        return 18.0;
    }

    private static bool IsBetterCandidate(
        CandidateRecognition candidate,
        CandidateRecognition currentBest)
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

        if (candidate.SegmentCount !=
            currentBest.SegmentCount)
        {
            return candidate.SegmentCount <
                   currentBest.SegmentCount;
        }

        return candidate.AverageScore >
               currentBest.AverageScore;
    }

    private static List<BenchmarkCard> SelectPreprocessedSample(
        List<BenchmarkCard> allCards,
        int? preprocessedSampleSize)
    {
        if (!preprocessedSampleSize.HasValue ||
            preprocessedSampleSize.Value <= 0 ||
            preprocessedSampleSize.Value >= allCards.Count)
        {
            return allCards;
        }

        const int sampleSeed = 20260717;

        var random =
            new Random(
                sampleSeed);

        return allCards
            .OrderBy(_ =>
                random.Next())
            .Take(
                preprocessedSampleSize.Value)
            .OrderBy(card =>
                card.DisplayId,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetPreprocessedSelectionDescription(
        int? preprocessedSampleSize,
        int actualCount)
    {
        if (!preprocessedSampleSize.HasValue)
        {
            return $"alle {actualCount} Preprocessed-Bilder";
        }

        return $"{actualCount} zufällig ausgewählte Preprocessed-Bilder";
    }

    private static List<BenchmarkCard> LoadAllPreprocessedImages(
        string previewFolder)
    {
        string[] files =
            Directory.GetFiles(
                previewFolder,
                "*.png",
                SearchOption.TopDirectoryOnly);

        var cards =
            new List<BenchmarkCard>();

        foreach (string imagePath in
                 files.OrderBy(
                     path => path,
                     StringComparer.OrdinalIgnoreCase))
        {
            string fileName =
                Path.GetFileNameWithoutExtension(
                    imagePath);

            string rawId =
                RemoveCandidateSuffix(
                    fileName);

            string expectedText =
                LocalCardDatabaseService
                    .GetPrintedCardNumber(
                        rawId)
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(
                    expectedText))
            {
                continue;
            }

            cards.Add(
                new BenchmarkCard
                {
                    DisplayId =
                        fileName,

                    ExpectedText =
                        expectedText,

                    ImagePaths =
                        [imagePath],

                    SourceDescription =
                        "jedes PNG im Preprocessed-Ordner einzeln"
                });
        }

        return cards;
    }

    private static List<BenchmarkCard> LoadBenchmarkCards(
        string previewFolder,
        string templateFolder)
    {
        string historyPath =
            Path.Combine(
                templateFolder,
                "training-history.txt");

        IEnumerable<string> sourceFiles;
        string sourceDescription;

        if (File.Exists(
                historyPath))
        {
            sourceFiles =
                File.ReadAllLines(
                    historyPath)
                .Where(line =>
                    !string.IsNullOrWhiteSpace(
                        line))
                .Select(line =>
                    line.Trim())
                .Where(File.Exists)
                .Where(path =>
                    string.Equals(
                        Path.GetExtension(path),
                        ".png",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase);

            sourceDescription =
                "nur manuell trainierte Bilder aus training-history.txt";
        }
        else
        {
            sourceFiles =
                Directory.GetFiles(
                    previewFolder,
                    "*.png",
                    SearchOption.TopDirectoryOnly);

            sourceDescription =
                "alle Bilder im Preprocessed-Ordner";
        }

        var entries =
            new List<BenchmarkEntry>();

        foreach (string imagePath in
                 sourceFiles)
        {
            string rawId =
                RemoveCandidateSuffix(
                    Path.GetFileNameWithoutExtension(
                        imagePath));

            string expectedText =
                LocalCardDatabaseService
                    .GetPrintedCardNumber(
                        rawId)
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(
                    expectedText))
            {
                continue;
            }

            entries.Add(
                new BenchmarkEntry
                {
                    ImagePath =
                        imagePath,

                    RawId =
                        rawId,

                    ExpectedText =
                        expectedText
                });
        }

        return entries
            .GroupBy(
                entry =>
                    entry.ExpectedText,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                new BenchmarkCard
                {
                    DisplayId =
                        group
                            .Select(entry =>
                                entry.RawId)
                            .OrderBy(value =>
                                value.Length)
                            .First(),

                    ExpectedText =
                        group.Key,

                    ImagePaths =
                        group
                            .Select(entry =>
                                entry.ImagePath)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList(),

                    SourceDescription =
                        sourceDescription
                })
            .OrderBy(card =>
                card.ExpectedText)
            .ToList();
    }

    private static void CountCharacterResults(
        string expected,
        string actual,
        IDictionary<(char Expected, char Actual), int>
            confusionCounts,
        ref int correctCharacters)
    {
        int comparableLength =
            Math.Min(
                expected.Length,
                actual.Length);

        for (int index = 0;
             index < comparableLength;
             index++)
        {
            if (expected[index] ==
                actual[index])
            {
                correctCharacters++;
            }
            else
            {
                AddConfusion(
                    confusionCounts,
                    expected[index],
                    actual[index]);
            }
        }

        for (int index = comparableLength;
             index < expected.Length;
             index++)
        {
            AddConfusion(
                confusionCounts,
                expected[index],
                '∅');
        }

        for (int index = comparableLength;
             index < actual.Length;
             index++)
        {
            AddConfusion(
                confusionCounts,
                '∅',
                actual[index]);
        }
    }

    private static void AddConfusion(
        IDictionary<(char Expected, char Actual), int>
            confusionCounts,
        char expected,
        char actual)
    {
        var key =
            (
                Expected: expected,
                Actual: actual);

        if (confusionCounts.TryGetValue(
                key,
                out int currentCount))
        {
            confusionCounts[key] =
                currentCount + 1;
        }
        else
        {
            confusionCounts[key] =
                1;
        }
    }

    private static BenchmarkFailure CreateBenchmarkFailure(
        BenchmarkCard card,
        CandidateRecognition? recognition)
    {
        return new BenchmarkFailure
        {
            ImageName =
                card.DisplayId,

            Expected =
                card.ExpectedText,

            Greedy =
                recognition?.GreedyText ?? string.Empty,

            DatabaseResult =
                recognition?.Text ?? string.Empty,

            ImageScore =
                recognition?.AverageScore ?? 0,

            DatabaseScore =
                recognition?.DatabaseScore ?? 0,

            OcrConfidence =
                recognition?.OcrConfidence ?? 0,

            DatabaseConfidence =
                recognition?.DatabaseConfidence ?? 0,

            OverallConfidence =
                recognition?.OverallConfidence ?? 0,

            ConfidenceMargin =
                recognition?.ConfidenceMargin ?? 0,

            SegmentCount =
                recognition?.SegmentCount ?? 0,

            SourceImagePath =
                recognition?.SourceImagePath ??
                card.ImagePaths.FirstOrDefault() ??
                string.Empty,

            FailureType =
                ClassifyFailure(
                    card.ExpectedText,
                    recognition),

            TopCandidates =
                recognition?.TopCandidates ??
                Array.Empty<DatabaseCandidateScore>()
        };
    }

    private static BenchmarkFailureType ClassifyFailure(
        string expected,
        CandidateRecognition? recognition)
    {
        if (recognition == null ||
            !recognition.IsUsable)
        {
            return BenchmarkFailureType.SegmentationFailed;
        }

        if (recognition.SegmentCount < expected.Length)
        {
            return BenchmarkFailureType.TooFewSegments;
        }

        if (recognition.SegmentCount > expected.Length)
        {
            return BenchmarkFailureType.TooManySegments;
        }

        if (string.Equals(
                expected,
                recognition.GreedyText,
                StringComparison.Ordinal) &&
            !string.Equals(
                expected,
                recognition.Text,
                StringComparison.Ordinal))
        {
            return BenchmarkFailureType.DatabaseWrongDecision;
        }

        string expectedPrefix =
            GetCardPrefix(
                expected);

        string actualPrefix =
            GetCardPrefix(
                recognition.Text);

        if (!string.Equals(
                expectedPrefix,
                actualPrefix,
                StringComparison.Ordinal))
        {
            return BenchmarkFailureType.PrefixMismatch;
        }

        bool hasDigitMismatch = false;
        bool hasLetterMismatch = false;
        bool hasOtherMismatch = false;

        int maximumLength =
            Math.Max(
                expected.Length,
                recognition.Text.Length);

        for (int index = 0;
             index < maximumLength;
             index++)
        {
            char expectedCharacter =
                index < expected.Length
                    ? expected[index]
                    : '∅';

            char actualCharacter =
                index < recognition.Text.Length
                    ? recognition.Text[index]
                    : '∅';

            if (expectedCharacter == actualCharacter)
            {
                continue;
            }

            if (char.IsDigit(expectedCharacter) ||
                char.IsDigit(actualCharacter))
            {
                hasDigitMismatch = true;
            }
            else if (char.IsLetter(expectedCharacter) ||
                     char.IsLetter(actualCharacter))
            {
                hasLetterMismatch = true;
            }
            else
            {
                hasOtherMismatch = true;
            }
        }

        if (hasDigitMismatch &&
            !hasLetterMismatch &&
            !hasOtherMismatch)
        {
            return BenchmarkFailureType.DigitMismatch;
        }

        if (hasLetterMismatch &&
            !hasDigitMismatch &&
            !hasOtherMismatch)
        {
            return BenchmarkFailureType.LetterMismatch;
        }

        if (recognition.ConfidenceMargin < 2.0)
        {
            return BenchmarkFailureType.LowConfidence;
        }

        if (hasDigitMismatch ||
            hasLetterMismatch ||
            hasOtherMismatch)
        {
            return BenchmarkFailureType.MixedMismatch;
        }

        return BenchmarkFailureType.Unknown;
    }

    private static string GetCardPrefix(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        return new string(
            value
                .TakeWhile(character =>
                    char.IsLetter(character))
                .ToArray());
    }

    private static void ReportProgress(
        IProgress<OcrBenchmarkProgress>? progress,
        int current,
        int total,
        string currentCard,
        int correctCards,
        int failedCards,
        TimeSpan elapsed)
    {
        TimeSpan? remaining =
            null;

        if (current > 0 &&
            total > current)
        {
            double secondsPerItem =
                elapsed.TotalSeconds /
                current;

            remaining =
                TimeSpan.FromSeconds(
                    secondsPerItem *
                    (total - current));
        }

        progress?.Report(
            new OcrBenchmarkProgress
            {
                Current =
                    current,

                Total =
                    total,

                CurrentCard =
                    currentCard,

                CorrectCards =
                    correctCards,

                FailedCards =
                    failedCards,

                Elapsed =
                    elapsed,

                EstimatedRemaining =
                    remaining
            });
    }

    private static string WriteReport(
        OcrBenchmarkMode mode,
        string dataSource,
        int testedCards,
        int correctCards,
        int failedCards,
        int correctCharacters,
        int totalCharacters,
        IReadOnlyDictionary<(char Expected, char Actual), int>
            confusionCounts,
        IReadOnlyList<string> segmentationFailures,
        IReadOnlyList<string> failedLines,
        IReadOnlyList<BenchmarkFailure> benchmarkFailures,
        int databaseCorrectedCount,
        int databaseWorsenedCount,
        int databaseUnchangedCorrectCount,
        int databaseUnchangedWrongCount,
        int lowConfidenceCount,
        TimeSpan duration)
    {
        double cardAccuracy =
            testedCards <= 0
                ? 0
                : correctCards *
                  100.0 /
                  testedCards;

        double characterAccuracy =
            totalCharacters <= 0
                ? 0
                : correctCharacters *
                  100.0 /
                  totalCharacters;

        var report =
            new StringBuilder();

        report.AppendLine(
            "========================================");

        report.AppendLine(
            mode == OcrBenchmarkMode.MatcherOnly
                ? "OCR BENCHMARK – MATCHER"
                : "OCR BENCHMARK – END TO END + DATABASE + WINDOW");

        report.AppendLine(
            "========================================");

        report.AppendLine();

        report.AppendLine(
            $"Datenquelle:              {dataSource}");

        report.AppendLine(
            $"Dauer:                    {duration:hh\\:mm\\:ss}");

        report.AppendLine();

        report.AppendLine(
            $"Elemente getestet:        {testedCards}");

        report.AppendLine(
            $"Vollständig richtig:      {correctCards}");

        report.AppendLine(
            $"Fehlgeschlagen/falsch:    {failedCards}");

        report.AppendLine(
            $"Gesamtgenauigkeit:        {cardAccuracy:0.00} %");

        report.AppendLine();

        report.AppendLine(
            $"Zeichen gesamt:           {totalCharacters}");

        report.AppendLine(
            $"Zeichen korrekt:          {correctCharacters}");

        report.AppendLine(
            $"Zeichengenauigkeit:       {characterAccuracy:0.00} %");

        if (mode == OcrBenchmarkMode.EndToEnd)
        {
            report.AppendLine();
            report.AppendLine(
                "----------------------------------------");

            report.AppendLine(
                "DATENBANK-EINFLUSS");

            report.AppendLine(
                "----------------------------------------");

            report.AppendLine(
                $"OCR durch DB korrigiert: {databaseCorrectedCount}");

            report.AppendLine(
                $"OCR durch DB verschlechtert: {databaseWorsenedCount}");

            report.AppendLine(
                $"Richtig und unverändert: {databaseUnchangedCorrectCount}");

            report.AppendLine(
                $"Falsch und unverändert:  {databaseUnchangedWrongCount}");

            report.AppendLine(
                $"Margin unter 2,0 Punkten: {lowConfidenceCount}");

            report.AppendLine();
            report.AppendLine(
                "----------------------------------------");

            report.AppendLine(
                "FEHLERKLASSEN");

            report.AppendLine(
                "----------------------------------------");

            foreach (var group in
                     benchmarkFailures
                         .GroupBy(failure =>
                             failure.FailureType)
                         .OrderByDescending(group =>
                             group.Count())
                         .ThenBy(group =>
                             group.Key))
            {
                report.AppendLine(
                    $"{group.Key,-28} {group.Count(),5}");
            }
        }

        report.AppendLine();
        report.AppendLine(
            "----------------------------------------");

        report.AppendLine(
            "HÄUFIGSTE VERWECHSLUNGEN");

        report.AppendLine(
            "----------------------------------------");

        foreach (var item in
                 confusionCounts
                     .OrderByDescending(pair =>
                         pair.Value)
                     .ThenBy(pair =>
                         pair.Key.Expected)
                     .Take(50))
        {
            report.AppendLine(
                $"{item.Key.Expected} -> " +
                $"{item.Key.Actual} : " +
                $"{item.Value}");
        }

        if (segmentationFailures.Count > 0)
        {
            report.AppendLine();
            report.AppendLine(
                "----------------------------------------");

            report.AppendLine(
                "SEGMENTIERUNGSFEHLER");

            report.AppendLine(
                "----------------------------------------");

            foreach (string line in
                     segmentationFailures)
            {
                report.AppendLine(
                    line);
            }
        }

        report.AppendLine();
        report.AppendLine(
            "----------------------------------------");

        report.AppendLine(
            "FALSCHE ERKENNUNGEN");

        report.AppendLine(
            "----------------------------------------");

        if (benchmarkFailures.Count == 0)
        {
            foreach (string line in
                     failedLines)
            {
                report.AppendLine(
                    line);
            }
        }
        else
        {
            foreach (BenchmarkFailure failure in
                     benchmarkFailures)
            {
                report.AppendLine();
                report.AppendLine(
                    "========================================");

                report.AppendLine(
                    $"Datei:               {failure.ImageName}");

                report.AppendLine(
                    $"Erwartet:            {failure.Expected}");

                report.AppendLine(
                    $"Greedy OCR:          {DisplayEmpty(failure.Greedy)}");

                report.AppendLine(
                    $"DB-Ergebnis:         {DisplayEmpty(failure.DatabaseResult)}");

                report.AppendLine(
                    $"Fehlerklasse:        {failure.FailureType}");

                report.AppendLine(
                    $"Segmente:            {failure.SegmentCount}");

                report.AppendLine(
                    $"Bild-Score:          {failure.ImageScore:0.0} %");

                report.AppendLine(
                    $"DB-Score:            {failure.DatabaseScore:0.0} %");

                report.AppendLine(
                    $"OCR-Confidence:      {failure.OcrConfidence:0.0} %");

                report.AppendLine(
                    $"DB-Confidence:       {failure.DatabaseConfidence:0.0} %");

                report.AppendLine(
                    $"Gesamt-Confidence:   {failure.OverallConfidence:0.0} %");

                report.AppendLine(
                    $"Abstand Platz 1/2:   {failure.ConfidenceMargin:0.00}");

                report.AppendLine(
                    $"Quellbild:           {Path.GetFileName(failure.SourceImagePath)}");

                report.AppendLine();
                report.AppendLine(
                    "Top-Datenbankkandidaten:");

                if (failure.TopCandidates.Count == 0)
                {
                    report.AppendLine(
                        "  keine Kandidaten");
                }
                else
                {
                    for (int candidateIndex = 0;
                         candidateIndex < failure.TopCandidates.Count;
                         candidateIndex++)
                    {
                        DatabaseCandidateScore candidate =
                            failure.TopCandidates[candidateIndex];

                        report.AppendLine(
                            $"  {candidateIndex + 1}. " +
                            $"{candidate.CardNumber,-12} " +
                            $"{candidate.Score:0.00} %");
                    }
                }
            }
        }

        string reportFolder =
            Path.Combine(
                GetSolutionFolder(),
                "Data",
                "OCRBenchmark");

        Directory.CreateDirectory(
            reportFolder);

        string modeName =
            mode == OcrBenchmarkMode.MatcherOnly
                ? "matcher"
                : dataSource.Contains(
                    "jedes PNG",
                    StringComparison.OrdinalIgnoreCase)
                    ? "all-images"
                    : "database";

        string reportPath =
            Path.Combine(
                reportFolder,
                $"ocr-benchmark-{modeName}-" +
                $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");

        File.WriteAllText(
            reportPath,
            report.ToString());

        return reportPath;
    }

    private static void SaveFailureArtifacts(
        string failureRunFolder,
        int failureIndex,
        BenchmarkFailure failure,
        CandidateRecognition? recognition)
    {
        try
        {
            string safeName = MakeSafePathPart(failure.ImageName);
            string safeExpected = MakeSafePathPart(failure.Expected);

            string failureFolder = Path.Combine(
                failureRunFolder,
                $"{failureIndex:0000}_{safeExpected}_{safeName}");

            Directory.CreateDirectory(failureFolder);

            if (File.Exists(failure.SourceImagePath))
            {
                File.Copy(
                    failure.SourceImagePath,
                    Path.Combine(failureFolder, "Original.png"),
                    overwrite: true);
            }

            WriteBytesIfAvailable(
                Path.Combine(failureFolder, "Region.png"),
                recognition?.RegionImagePng);

            WriteBytesIfAvailable(
                Path.Combine(failureFolder, "Binary.png"),
                recognition?.BinaryImagePng);

            IReadOnlyList<byte[]> segmentImages =
                recognition?.SegmentImagesPng ?? Array.Empty<byte[]>();

            for (int segmentIndex = 0;
                 segmentIndex < segmentImages.Count;
                 segmentIndex++)
            {
                WriteBytesIfAvailable(
                    Path.Combine(
                        failureFolder,
                        $"Segment{segmentIndex + 1:00}.png"),
                    segmentImages[segmentIndex]);
            }

            CreateSegmentOverview(
                segmentImages,
                Path.Combine(failureFolder, "Segments.png"));

            File.WriteAllText(
                Path.Combine(failureFolder, "info.txt"),
                BuildFailureInfo(failure),
                Encoding.UTF8);
        }
        catch
        {
            /* Diagnosebilder dürfen den Benchmark nicht abbrechen. */
        }
    }

    private static string BuildFailureInfo(
        BenchmarkFailure failure)
    {
        var info = new StringBuilder();

        info.AppendLine($"Datei: {failure.ImageName}");
        info.AppendLine($"Erwartet: {failure.Expected}");
        info.AppendLine($"Greedy OCR: {DisplayEmpty(failure.Greedy)}");
        info.AppendLine($"DB-Ergebnis: {DisplayEmpty(failure.DatabaseResult)}");
        info.AppendLine($"Fehlerklasse: {failure.FailureType}");
        info.AppendLine($"Segmente: {failure.SegmentCount}");
        info.AppendLine($"Bild-Score: {failure.ImageScore:0.0} %");
        info.AppendLine($"DB-Score: {failure.DatabaseScore:0.0} %");
        info.AppendLine($"OCR-Confidence: {failure.OcrConfidence:0.0} %");
        info.AppendLine($"DB-Confidence: {failure.DatabaseConfidence:0.0} %");
        info.AppendLine($"Gesamt-Confidence: {failure.OverallConfidence:0.0} %");
        info.AppendLine($"Abstand Platz 1/2: {failure.ConfidenceMargin:0.00}");
        info.AppendLine($"Quellbild: {failure.SourceImagePath}");
        info.AppendLine();
        info.AppendLine("Top-Datenbankkandidaten:");

        if (failure.TopCandidates.Count == 0)
        {
            info.AppendLine("  keine Kandidaten");
        }
        else
        {
            for (int index = 0;
                 index < failure.TopCandidates.Count;
                 index++)
            {
                DatabaseCandidateScore candidate = failure.TopCandidates[index];
                info.AppendLine(
                    $"  {index + 1}. {candidate.CardNumber} | " +
                    $"{candidate.Score:0.00} %");
            }
        }

        return info.ToString();
    }

    private static byte[] ReadFileBytesSafely(
        string filePath)
    {
        try
        {
            return File.Exists(filePath)
                ? File.ReadAllBytes(filePath)
                : Array.Empty<byte>();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static byte[] CreateBinaryPreviewPng(
        string regionPath)
    {
        try
        {
            using Mat source = Cv2.ImRead(
                regionPath,
                ImreadModes.Grayscale);

            if (source.Empty())
            {
                return Array.Empty<byte>();
            }

            using Mat blurred = new();
            Cv2.GaussianBlur(source, blurred, new Size(3, 3), 0);

            using Mat binary = new();
            Cv2.Threshold(
                blurred,
                binary,
                0,
                255,
                ThresholdTypes.Binary | ThresholdTypes.Otsu);

            int whitePixels = Cv2.CountNonZero(binary);
            int totalPixels = Math.Max(1, binary.Rows * binary.Cols);

            if (whitePixels > totalPixels / 2)
            {
                Cv2.BitwiseNot(binary, binary);
            }

            if (Cv2.ImEncode(".png", binary, out byte[] encoded))
            {
                return encoded;
            }

            return Array.Empty<byte>();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static IReadOnlyList<byte[]> EncodeSegmentImages(
        IReadOnlyList<CharacterSegment> segments)
    {
        var result = new List<byte[]>(segments.Count);

        foreach (CharacterSegment segment in segments)
        {
            try
            {
                if (segment.Image.Empty())
                {
                    result.Add(Array.Empty<byte>());
                }
                else if (Cv2.ImEncode(".png", segment.Image, out byte[] encoded))
                {
                    result.Add(encoded);
                }
                else
                {
                    result.Add(Array.Empty<byte>());
                }
            }
            catch
            {
                result.Add(Array.Empty<byte>());
            }
        }

        return result;
    }

    private static void CreateSegmentOverview(
        IReadOnlyList<byte[]> segmentImages,
        string outputPath)
    {
        var decoded = new List<Mat>();

        try
        {
            foreach (byte[] imageBytes in segmentImages)
            {
                if (imageBytes.Length == 0)
                {
                    continue;
                }

                Mat image = Cv2.ImDecode(
                    imageBytes,
                    ImreadModes.Grayscale);

                if (image.Empty())
                {
                    image.Dispose();
                    continue;
                }

                decoded.Add(image);
            }

            if (decoded.Count == 0)
            {
                return;
            }

            const int spacing = 6;
            const int outerPadding = 8;

            int maximumHeight = decoded.Max(image => image.Height);
            int canvasWidth =
                outerPadding * 2 +
                decoded.Sum(image => image.Width) +
                spacing * Math.Max(0, decoded.Count - 1);
            int canvasHeight = maximumHeight + outerPadding * 2;

            using Mat canvas = new(
                canvasHeight,
                canvasWidth,
                MatType.CV_8UC1,
                Scalar.Black);

            int x = outerPadding;

            foreach (Mat image in decoded)
            {
                int y = outerPadding + (maximumHeight - image.Height) / 2;

                using Mat target = new(
                    canvas,
                    new Rect(x, y, image.Width, image.Height));

                image.CopyTo(target);
                x += image.Width + spacing;
            }

            Cv2.ImWrite(outputPath, canvas);
        }
        catch
        {
            /* Die Übersicht ist optional. */
        }
        finally
        {
            foreach (Mat image in decoded)
            {
                image.Dispose();
            }
        }
    }

    private static void WriteBytesIfAvailable(
        string outputPath,
        byte[]? content)
    {
        if (content == null || content.Length == 0)
        {
            return;
        }

        File.WriteAllBytes(outputPath, content);
    }

    private static string MakeSafePathPart(
        string value)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();

        string safe = new(
            value
                .Select(character =>
                    invalidCharacters.Contains(character)
                        ? '_'
                        : character)
                .ToArray());

        return string.IsNullOrWhiteSpace(safe)
            ? "unbekannt"
            : safe;
    }

    private static string DisplayEmpty(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "<leer>"
            : value;
    }

    private static string RemoveCandidateSuffix(
        string fileName)
    {
        return Regex.Replace(
            fileName,
            @"_c\d+$",
            string.Empty,
            RegexOptions.IgnoreCase);
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
             * Temporäre Dateien dürfen den Benchmark
             * nicht abbrechen.
             */
        }
    }

    private static string GetSolutionFolder()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                @"..\..\..\.."));
    }

    private static void DisposeLibrary(
        CharacterTemplateLibrary library)
    {
        foreach (CharacterTemplate template in
                 library.GetAllTemplates())
        {
            template.Image.Dispose();
        }
    }

    private sealed class BenchmarkEntry
    {
        public string ImagePath { get; init; } =
            string.Empty;

        public string RawId { get; init; } =
            string.Empty;

        public string ExpectedText { get; init; } =
            string.Empty;
    }

    private sealed class BenchmarkCard
    {
        public string DisplayId { get; init; } =
            string.Empty;

        public string ExpectedText { get; init; } =
            string.Empty;

        public List<string> ImagePaths { get; init; } =
            [];

        public string SourceDescription { get; init; } =
            string.Empty;
    }

    private sealed class CandidateRecognition
    {
        public string SourceImagePath { get; init; } =
            string.Empty;

        public byte[] RegionImagePng { get; init; } =
            Array.Empty<byte>();

        public byte[] BinaryImagePng { get; init; } =
            Array.Empty<byte>();

        public IReadOnlyList<byte[]> SegmentImagesPng { get; init; } =
            Array.Empty<byte[]>();

        public bool IsUsable { get; init; }

        public string Text { get; init; } =
            string.Empty;

        public string GreedyText { get; init; } =
            string.Empty;

        public double AverageScore { get; init; }

        public double DatabaseScore { get; init; }

        public int SegmentCount { get; init; }

        public int LengthDifference { get; init; }

        public IReadOnlyList<DatabaseCandidateScore> TopCandidates { get; init; } =
            Array.Empty<DatabaseCandidateScore>();

        public double ConfidenceMargin { get; init; }

        public double OcrConfidence { get; init; }

        public double DatabaseConfidence { get; init; }

        public double OverallConfidence { get; init; }
    }

    private sealed class BenchmarkFailure
    {
        public string ImageName { get; init; } =
            string.Empty;

        public string Expected { get; init; } =
            string.Empty;

        public string Greedy { get; init; } =
            string.Empty;

        public string DatabaseResult { get; init; } =
            string.Empty;

        public double ImageScore { get; init; }

        public double DatabaseScore { get; init; }

        public double OcrConfidence { get; init; }

        public double DatabaseConfidence { get; init; }

        public double OverallConfidence { get; init; }

        public double ConfidenceMargin { get; init; }

        public int SegmentCount { get; init; }

        public string SourceImagePath { get; init; } =
            string.Empty;

        public BenchmarkFailureType FailureType { get; init; }

        public IReadOnlyList<DatabaseCandidateScore> TopCandidates { get; init; } =
            Array.Empty<DatabaseCandidateScore>();
    }

    private sealed class DatabaseCandidateScore
    {
        public string CardNumber { get; init; } =
            string.Empty;

        public double Score { get; init; }
    }
}
