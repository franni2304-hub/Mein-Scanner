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
using System.Text.RegularExpressions;

namespace OnePieceCardScanner.Recognition.OCR.Debugging;

public sealed class OcrDebugPipeline : IDisposable
{
    private readonly CharacterSegmenter _segmenter = new();
    private readonly WideSegmentSplitter _wideSegmentSplitter = new();
    private readonly CardNumberRegionDetector _regionDetector = new();
    private readonly CharacterTemplateLibrary _templateLibrary;
    private readonly CharacterMatcher _matcher;
    private bool _isDisposed;

    public OcrDebugPipeline()
    {
        string templateFolder = Path.Combine(
            GetSolutionFolder(), "Data", "OCRTemplates");

        _templateLibrary = new CharacterTemplateLibrary();
        _templateLibrary.Load(templateFolder);
        _matcher = new CharacterMatcher(_templateLibrary);
    }

    public OcrDebugResult Analyze(string imagePath)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Das Bild wurde nicht gefunden.", imagePath);

        string expectedText = GetExpectedCardNumber(imagePath);

        using Mat original = Cv2.ImRead(imagePath, ImreadModes.Grayscale);
        if (original.Empty())
            throw new InvalidOperationException("Das Bild konnte nicht geladen werden.");

        string temporaryFolder = Path.Combine(
            GetSolutionFolder(), "Data", "OCRDebug", "Temp",
            Guid.NewGuid().ToString("N"));

        try
        {
            IReadOnlyList<string> regionPaths = _regionDetector.CreateCandidateImages(
                imagePath,
                Math.Max(1, expectedText.Length),
                temporaryFolder);

            var regions = new List<OcrDebugRegionResult>();

            for (int regionIndex = 0; regionIndex < regionPaths.Count; regionIndex++)
            {
                regions.Add(AnalyzeRegion(
                    regionIndex,
                    regionPaths[regionIndex],
                    expectedText.Length));
            }

            OcrDebugRegionResult? bestRegion = regions
                .OrderBy(region => region.LengthDifference)
                .ThenByDescending(region => region.AverageScore)
                .FirstOrDefault();

            return new OcrDebugResult
            {
                ImagePath = imagePath,
                ExpectedText = expectedText,
                OriginalImagePng = EncodePng(original),
                Regions = regions,
                BestRegionIndex = bestRegion?.Index ?? -1
            };
        }
        finally
        {
            TryDeleteDirectory(temporaryFolder);
        }
    }

    private OcrDebugRegionResult AnalyzeRegion(
        int regionIndex,
        string regionPath,
        int expectedLength)
    {
        using Mat regionImage = Cv2.ImRead(regionPath, ImreadModes.Grayscale);
        IReadOnlyList<CharacterSegment> initialSegments = _segmenter.Segment(regionPath);
        IReadOnlyList<CharacterSegment> segments =
            _wideSegmentSplitter.SplitWideSegments(initialSegments);

        try
        {
            var segmentResults = new List<OcrDebugSegmentResult>();
            var greedyText = new StringBuilder();
            double scoreSum = 0;

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                CharacterSegment segment = segments[segmentIndex];
                IReadOnlyList<CharacterMatch> matches = _matcher.Match(segment.Image, top: 5);
                var matchResults = new List<OcrDebugMatchResult>();

                foreach (CharacterMatch match in matches)
                {
                    byte[] templatePng = match.BestTemplate?.Image != null &&
                                         !match.BestTemplate.Image.Empty()
                        ? EncodePng(match.BestTemplate.Image)
                        : [];

                    matchResults.Add(new OcrDebugMatchResult
                    {
                        Character = match.Character,
                        Score = match.Score,
                        BestTemplateScore = match.BestTemplateScore,
                        TemplateFilePath = match.BestTemplate?.FilePath ?? string.Empty,
                        TemplateImagePng = templatePng
                    });
                }

                if (matches.Count > 0)
                {
                    greedyText.Append(matches[0].Character);
                    scoreSum += matches[0].Score;
                }
                else
                {
                    greedyText.Append('?');
                }

                segmentResults.Add(new OcrDebugSegmentResult
                {
                    Index = segmentIndex,
                    SegmentImagePng = EncodePng(segment.Image),
                    Matches = matchResults
                });
            }

            return new OcrDebugRegionResult
            {
                Index = regionIndex,
                RegionPath = regionPath,
                RegionImagePng = regionImage.Empty() ? [] : EncodePng(regionImage),
                GreedyText = greedyText.ToString(),
                SegmentCount = segments.Count,
                ExpectedLength = expectedLength,
                LengthDifference = Math.Abs(segments.Count - expectedLength),
                AverageScore = segments.Count == 0 ? 0 : scoreSum / segments.Count,
                Segments = segmentResults
            };
        }
        finally
        {
            foreach (CharacterSegment segment in segments)
                segment.Image.Dispose();
        }
    }

    private static string GetExpectedCardNumber(string imagePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(imagePath);
        fileName = Regex.Replace(fileName, @"_c\d+$", string.Empty, RegexOptions.IgnoreCase);

        return LocalCardDatabaseService
            .GetPrintedCardNumber(fileName)
            .Trim()
            .ToUpperInvariant();
    }

    private static byte[] EncodePng(Mat image)
    {
        Cv2.ImEncode(".png", image, out byte[] bytes);
        return bytes;
    }

    private static string GetSolutionFolder()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\.."));
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // Temporäre Debug-Dateien dürfen die Analyse nicht abbrechen.
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _matcher.Dispose();

        foreach (CharacterTemplate template in _templateLibrary.GetAllTemplates())
            template.Image.Dispose();

        _isDisposed = true;
    }
}

public sealed class OcrDebugResult
{
    public string ImagePath { get; init; } = string.Empty;
    public string ExpectedText { get; init; } = string.Empty;
    public byte[] OriginalImagePng { get; init; } = [];
    public IReadOnlyList<OcrDebugRegionResult> Regions { get; init; } = [];
    public int BestRegionIndex { get; init; } = -1;
}

public sealed class OcrDebugRegionResult
{
    public int Index { get; init; }
    public string RegionPath { get; init; } = string.Empty;
    public byte[] RegionImagePng { get; init; } = [];
    public string GreedyText { get; init; } = string.Empty;
    public int SegmentCount { get; init; }
    public int ExpectedLength { get; init; }
    public int LengthDifference { get; init; }
    public double AverageScore { get; init; }
    public IReadOnlyList<OcrDebugSegmentResult> Segments { get; init; } = [];
}

public sealed class OcrDebugSegmentResult
{
    public int Index { get; init; }
    public byte[] SegmentImagePng { get; init; } = [];
    public IReadOnlyList<OcrDebugMatchResult> Matches { get; init; } = [];
}

public sealed class OcrDebugMatchResult
{
    public char Character { get; init; }
    public double Score { get; init; }
    public double BestTemplateScore { get; init; }
    public string TemplateFilePath { get; init; } = string.Empty;
    public byte[] TemplateImagePng { get; init; } = [];
}