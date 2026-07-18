using OnePieceCardScanner.Recognition.TemplateMatching;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceCardScanner.Recognition.OCR.Core;

public sealed class DatabaseCardMatchResult
{
    public string CardNumber { get; init; } =
        string.Empty;

    public double Score { get; init; }

    public bool Success =>
        !string.IsNullOrWhiteSpace(
            CardNumber);
}

public sealed class DatabaseCardMatcher
{
    private const int MaximumCandidates = 100;
    private const double MissingCharacterPenalty = 28.0;
    private const double ExtraSegmentPenalty = 16.0;

    private readonly IReadOnlyList<string> _knownCardNumbers;

    public DatabaseCardMatcher(
        IReadOnlyList<string> knownCardNumbers)
    {
        _knownCardNumbers =
            knownCardNumbers ??
            throw new ArgumentNullException(
                nameof(knownCardNumbers));
    }

    public DatabaseCardMatchResult FindBestMatch(
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        if (string.IsNullOrWhiteSpace(
                greedyText) ||
            alternatives.Count == 0 ||
            _knownCardNumbers.Count == 0)
        {
            return new DatabaseCardMatchResult();
        }

        List<string> candidatePool =
            SelectLikelyCandidates(
                greedyText,
                alternatives);

        DatabaseCardMatchResult? best =
            null;

        foreach (string cardNumber in
                 candidatePool)
        {
            double score =
                ScoreCandidate(
                    cardNumber,
                    greedyText,
                    alternatives);

            if (best == null ||
                score > best.Score)
            {
                best =
                    new DatabaseCardMatchResult
                    {
                        CardNumber =
                            cardNumber,

                        Score =
                            score
                    };
            }
        }

        return best ??
               new DatabaseCardMatchResult();
    }

    private List<string> SelectLikelyCandidates(
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        int segmentCount =
            alternatives.Count;

        return _knownCardNumbers
            .Where(cardNumber =>
                cardNumber.Length >=
                    Math.Max(
                        3,
                        segmentCount - 5) &&
                cardNumber.Length <=
                    segmentCount + 2)
            .Select(cardNumber =>
                new
                {
                    CardNumber =
                        cardNumber,

                    Score =
                        CalculateQuickScore(
                            cardNumber,
                            greedyText,
                            alternatives)
                })
            .OrderByDescending(item =>
                item.Score)
            .Take(
                MaximumCandidates)
            .Select(item =>
                item.CardNumber)
            .ToList();
    }

    private static double CalculateQuickScore(
        string cardNumber,
        string greedyText,
        IReadOnlyList<IReadOnlyList<CharacterMatch>> alternatives)
    {
        double alignedScore =
            CalculateBestAlignedScore(
                cardNumber,
                alternatives);

        double windowSimilarity =
            CalculateBestWindowSimilarity(
                greedyText,
                cardNumber);

        return alignedScore *
               0.65 +
               windowSimilarity *
               0.35;
    }

    private static double ScoreCandidate(
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

        double exactSubstringBonus =
            greedyText.Contains(
                cardNumber,
                StringComparison.OrdinalIgnoreCase)
                ? 8.0
                : 0.0;

        return Math.Clamp(
            alignmentScore *
            0.65 +
            windowSimilarity *
            0.35 +
            exactSubstringBonus,
            0,
            100);
    }

    private static double CalculateBestAlignedScore(
        string cardNumber,
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
                 characterIndex < cardNumber.Length;
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
                    cardNumber[
                        characterIndex];

                CharacterMatch? exactMatch =
                    alternatives[segmentIndex]
                        .FirstOrDefault(match =>
                            match.Character ==
                            expectedCharacter);

                score +=
                    exactMatch?.Score ??
                    18.0;
            }

            int usedStart =
                Math.Max(
                    0,
                    offset);

            int usedEnd =
                Math.Min(
                    alternatives.Count,
                    cardNumber.Length +
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
                    cardNumber.Length);

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
}
