using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Recognition;

public sealed class CardVariantResolver
{
    private readonly CardImageMatcher _matcher =
        new();

    public LocalCardData? Resolve(
        string detectedCardPath,
        IReadOnlyList<LocalCardData> variants)
    {
        if (variants.Count == 0)
            return null;

        if (variants.Count == 1)
            return variants[0];

        double bestScore = double.MinValue;
        LocalCardData? bestCard = null;

        foreach (LocalCardData card in variants)
        {
            if (string.IsNullOrWhiteSpace(card.LocalImagePath))
                continue;

            if (!File.Exists(card.LocalImagePath))
                continue;

            double score =
                _matcher.Compare(
                    detectedCardPath,
                    card.LocalImagePath);

            System.Diagnostics.Debug.WriteLine(
                $"{card.Id} -> {score:0.00}%");

            if (score > bestScore)
            {
                bestScore = score;
                bestCard = card;
            }
        }

        return bestCard;
    }
}