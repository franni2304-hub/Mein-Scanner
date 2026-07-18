using System;
using System.Collections.Generic;
using System.Text;
using OnePieceCardScanner.Models;
using OnePieceCardScanner.Repositories;
using OnePieceCardScanner.Enums;

namespace OnePieceCardScanner.Services;

public sealed class InventoryManager
{
    private readonly InventoryIdService _inventoryIdService =
        new();

    private readonly CollectionItemRepository _repository =
        new();

    public CollectionItem CreateInventoryItem(CardData card)
    {
        CollectionItem item = new()
        {
            InventoryId = _inventoryIdService.GenerateNextId(),

            CardDataId = card.Id,

            Condition = CardCondition.NM,

            Status = InventoryStatus.STORED
        };

        _repository.Add(item);

        return item;
    }
}