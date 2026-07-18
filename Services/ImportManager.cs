using OnePieceCardScanner.Models;
using OnePieceCardScanner.Recognition;
using System;
using System.Linq;

namespace OnePieceCardScanner.Services.Import;

public sealed class ImportManager
{
    private readonly CardRecognitionService _recognition =
        new();

    private readonly PunkRecordsService _punkRecords =
        new();

    private readonly InventoryManager _inventory =
        new();

    public CollectionItem? Import(string imagePath)
    {
        RecognitionResult result =
            _recognition.Recognize(imagePath);

        List<PunkRecordCard> matches =
            _punkRecords.FindByCardNumber(
                result.CardNumber);

        if (matches.Count == 0)
            return null;

        PunkRecordCard? punkCard =
        matches.FirstOrDefault(card =>
        card.Id.Equals(
            result.CardNumber,
            StringComparison.OrdinalIgnoreCase));

        if (punkCard == null)
        {
            punkCard = matches[0];
        }

        CardData card = new()
        {
            Id = punkCard.Id,
            CardNumber = punkCard.Id,
            Name = punkCard.Name,
            Language = "ENG",
            SetCode = GetSetCode(punkCard.Id),
            Rarity = punkCard.Rarity,
            CardType = punkCard.Category,
            Variant = "BASE",
            ArtworkVariant = "BASE",
            Finish = "NORMAL",
            ProductCode = punkCard.PackId,
            ImageUrl = punkCard.ImgFullUrl
        };

        return _inventory.CreateInventoryItem(card);
    }

    private static string GetSetCode(string cardId)
    {
        int separatorIndex = cardId.IndexOf('-');

        if (separatorIndex <= 0)
            return string.Empty;

        return cardId[..separatorIndex];
    }
}