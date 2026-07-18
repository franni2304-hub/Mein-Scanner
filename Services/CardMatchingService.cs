using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Services;

public sealed class CardMatchingService
{
    public LocalCardData? FindBestMatch(
        string cardNumber,
        string rarity)
    {
        IReadOnlyList<LocalCardData> matches =
            ServiceLocator.Database.FindById(
                cardNumber);

        if (matches.Count == 0)
            return null;

        if (matches.Count == 1)
            return matches[0];

        if (!string.IsNullOrWhiteSpace(rarity) &&
            !rarity.Equals(
                "UNBEKANNT",
                StringComparison.OrdinalIgnoreCase))
        {
            List<LocalCardData> rarityMatches =
                matches
                    .Where(card =>
                        string.Equals(
                            card.Rarity,
                            rarity,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (rarityMatches.Count == 1)
                return rarityMatches[0];

            if (rarityMatches.Count > 1)
                return rarityMatches[0];
        }

        return null;
    }
}