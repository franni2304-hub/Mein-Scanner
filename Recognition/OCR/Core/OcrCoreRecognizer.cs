using OnePieceCardScanner.Recognition.Segmentation;
using OnePieceCardScanner.Recognition.TemplateMatching;
using OnePieceCardScanner.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OnePieceCardScanner.Recognition.OCR.Core;

public sealed class OcrCoreRecognizer : IDisposable
{
    private const int TopMatchesPerSegment = 3;

    private readonly CharacterSegmenter _segmenter =
        new();

    private readonly WideSegmentSplitter _wideSegmentSplitter =
        new();

    private readonly CharacterTemplateLibrary _templateLibrary;

    private readonly CharacterMatcher _matcher;

    private readonly IReadOnlyList<string> _knownCardNumbers;

    private bool _isDisposed;

    public OcrCoreRecognizer()
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
                .Where(cardNumber =>
                    !string.IsNullOrWhiteSpace(
                        cardNumber))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(cardNumber =>
                    cardNumber,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    public OcrCoreResult RecognizeRegion(
        string regionImagePath)
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        if (!File.Exists(
                regionImagePath))
        {
            throw new FileNotFoundException(
                "Das Regionsbild wurde nicht gefunden.",
                regionImagePath);
        }

        IReadOnlyList<CharacterSegment> initialSegments =
            _segmenter.Segment(
                regionImagePath);

        IReadOnlyList<CharacterSegment> segments =
            _wideSegmentSplitter.SplitWideSegments(
                initialSegments);

        try
        {
            if (segments.Count == 0)
            {
                return new OcrCoreResult();
            }

            var greedyText =
                new StringBuilder();

            double imageScore =
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
                    return new OcrCoreResult
                    {
                        SegmentCount =
                            segments.Count
                    };
                }

                CharacterMatch bestMatch =
                    matches[0];

                greedyText.Append(
                    bestMatch.Character);

                imageScore +=
                    bestMatch.Score;
            }

            string greedy =
                greedyText.ToString();

            string bestCardNumber =
                FindNearestCardNumber(
                    greedy);

            double confidence =
                CalculateTextSimilarity(
                    greedy,
                    bestCardNumber);

            return new OcrCoreResult
            {
                CardNumber =
                    bestCardNumber,

                GreedyText =
                    greedy,

                Confidence =
                    confidence,

                ImageScore =
                    imageScore /
                    segments.Count,

                SegmentCount =
                    segments.Count
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

    private string FindNearestCardNumber(
        string greedyText)
    {
        if (string.IsNullOrWhiteSpace(
                greedyText) ||
            _knownCardNumbers.Count == 0)
        {
            return string.Empty;
        }

        return _knownCardNumbers
            .Select(cardNumber =>
                new
                {
                    CardNumber =
                        cardNumber,

                    Distance =
                        CalculateLevenshteinDistance(
                            greedyText,
                            cardNumber),

                    LengthDifference =
                        Math.Abs(
                            greedyText.Length -
                            cardNumber.Length)
                })
            .OrderBy(item =>
                item.Distance)
            .ThenBy(item =>
                item.LengthDifference)
            .ThenBy(item =>
                item.CardNumber,
                StringComparer.OrdinalIgnoreCase)
            .Select(item =>
                item.CardNumber)
            .FirstOrDefault()
            ?? string.Empty;
    }

    private static double CalculateTextSimilarity(
        string recognizedText,
        string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(
                recognizedText) ||
            string.IsNullOrWhiteSpace(
                cardNumber))
        {
            return 0;
        }

        int distance =
            CalculateLevenshteinDistance(
                recognizedText,
                cardNumber);

        int maximumLength =
            Math.Max(
                recognizedText.Length,
                cardNumber.Length);

        if (maximumLength == 0)
        {
            return 100;
        }

        return Math.Clamp(
            100.0 *
            (1.0 -
             distance /
             (double)maximumLength),
            0,
            100);
    }

    private static int CalculateLevenshteinDistance(
        string left,
        string right)
    {
        left =
            left.ToUpperInvariant();

        right =
            right.ToUpperInvariant();

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
                    left[leftIndex - 1] ==
                    right[rightIndex - 1]
                        ? 0
                        : 1;

                int deletion =
                    distances[
                        leftIndex - 1,
                        rightIndex] + 1;

                int insertion =
                    distances[
                        leftIndex,
                        rightIndex - 1] + 1;

                int substitution =
                    distances[
                        leftIndex - 1,
                        rightIndex - 1] +
                    substitutionCost;

                distances[
                    leftIndex,
                    rightIndex] =
                    Math.Min(
                        Math.Min(
                            deletion,
                            insertion),
                        substitution);
            }
        }

        return distances[
            left.Length,
            right.Length];
    }

    private static string GetSolutionFolder()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                @"..\..\..\.."));
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

    public string CardNumber { get; init; } = string.Empty;
    public string GreedyText { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public double ImageScore { get; init; }
    public int SegmentCount { get; init; }
}
