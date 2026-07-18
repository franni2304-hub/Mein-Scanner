using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Storage;

public sealed class StorageManager
{
    public StoragePosition FindNextFreePosition(
        IReadOnlyList<StorageContainer> containers,
        IReadOnlyList<CollectionItem> inventoryItems)
    {
        foreach (StorageContainer container in containers
                     .Where(container => container.IsActive)
                     .OrderBy(container => container.Code))
        {
            HashSet<int> occupiedSlots = inventoryItems
                .Where(item =>
                    item.ContainerCode != null &&
                    item.ContainerCode.Equals(
                        container.Code,
                        StringComparison.OrdinalIgnoreCase) &&
                    item.SlotNumber.HasValue)
                .Select(item => item.SlotNumber!.Value)
                .ToHashSet();

            for (int slotNumber = 1;
                 slotNumber <= container.Capacity;
                 slotNumber++)
            {
                if (!occupiedSlots.Contains(slotNumber))
                {
                    return new StoragePosition
                    {
                        ContainerCode = container.Code,
                        SlotNumber = slotNumber
                    };
                }
            }
        }

        throw new InvalidOperationException(
            "Es wurde kein freier Lagerplatz gefunden.");
    }

    public void AssignPosition(
        CollectionItem inventoryItem,
        StoragePosition position)
    {
        inventoryItem.ContainerCode = position.ContainerCode;
        inventoryItem.SlotNumber = position.SlotNumber;
    }
}