using System;
using System.Collections.Generic;
using System.Text;
using OnePieceCardScanner.Models;
using OnePieceCardScanner.Repositories;
using OnePieceCardScanner.ViewModels;

namespace OnePieceCardScanner.Services;

public sealed class InventoryService
{
    private readonly CollectionItemRepository _repository =
        new();

    private readonly CardDatabaseService _cardDatabase =
        new();

    public List<InventoryItemViewModel> GetInventory()
    {
        List<InventoryItemViewModel> result = new();

        List<CollectionItem> items =
            _repository.GetAll();

        foreach (CollectionItem item in items)
        {
            CardData? card =
                _cardDatabase.FindById(item.CardDataId);

            if (card == null)
                continue;

            result.Add(new InventoryItemViewModel
            {
                InventoryId = item.InventoryId,

                CardNumber = card.CardNumber,

                Name = card.Name,

                Language = card.Language,

                Variant = card.Variant,

                Condition = item.Condition.ToString(),

                Status = item.Status.ToString(),

                StorageCode = item.GetStorageCode() ?? "-",

                ImageUrl = card.ImageUrl
            });
        }

        return result;
    }
}
