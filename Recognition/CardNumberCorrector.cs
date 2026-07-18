using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceCardScanner.Models;
using OnePieceCardScanner.Services;

namespace OnePieceCardScanner.Recognition;

public static class CardNumberCorrector
{
    public static string Correct(string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            return cardNumber;

        string normalized =
            NormalizeCommonMistakes(cardNumber);

        if (ServiceLocator.Database.FindById(normalized).Count > 0)
            return normalized;

        string? setPrefix =
            GetSetPrefix(normalized);

        IEnumerable<LocalCardData> candidates =
            ServiceLocator.Database.GetAllCards();

        if (!string.IsNullOrWhiteSpace(setPrefix))
        {
            candidates = candidates.Where(card =>
                card.Id.StartsWith(
                    setPrefix,
                    StringComparison.OrdinalIgnoreCase));
        }

        string? bestMatch = null;
        int bestDistance = int.MaxValue;
        bool ambiguous = false;

        foreach (string candidateId in candidates
                     .Select(card => card.Id)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int distance =
                CalculateLevenshteinDistance(
                    normalized,
                    candidateId);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = candidateId;
                ambiguous = false;
            }
            else if (distance == bestDistance)
            {
                ambiguous = true;
            }
        }

        // Nur eindeutig und mit maximal einem Fehler korrigieren.
        if (bestMatch != null &&
            bestDistance <= 1 &&
            !ambiguous)
        {
            return bestMatch;
        }

        return normalized;
    }

    private static string NormalizeCommonMistakes(
        string value)
    {
        string text = value
            .ToUpperInvariant()
            .Replace(" ", string.Empty)
            .Replace("_", "-")
            .Replace("—", "-")
            .Replace("–", "-");

        int separatorIndex =
            text.LastIndexOf('-');

        if (separatorIndex < 0)
            return text;

        string prefix =
            text[..(separatorIndex + 1)];

        string numberPart =
            text[(separatorIndex + 1)..]
                .Replace('O', '0')
                .Replace('I', '1')
                .Replace('L', '1')
                .Replace('B', '8')
                .Replace('S', '5')
                .Replace('Z', '2');

        return prefix + numberPart;
    }

    private static string? GetSetPrefix(
        string cardNumber)
    {
        int separatorIndex =
            cardNumber.IndexOf('-');

        if (separatorIndex <= 0)
            return null;

        return cardNumber[..(separatorIndex + 1)];
    }

    private static int CalculateLevenshteinDistance(
        string left,
        string right)
    {
        int[,] distances =
            new int[left.Length + 1, right.Length + 1];

        for (int i = 0; i <= left.Length; i++)
            distances[i, 0] = i;

        for (int j = 0; j <= right.Length; j++)
            distances[0, j] = j;

        for (int i = 1; i <= left.Length; i++)
        {
            for (int j = 1; j <= right.Length; j++)
            {
                int substitutionCost =
                    left[i - 1] == right[j - 1]
                        ? 0
                        : 1;

                distances[i, j] = Math.Min(
                    Math.Min(
                        distances[i - 1, j] + 1,
                        distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] +
                    substitutionCost);
            }
        }

        return distances[left.Length, right.Length];
    }
}