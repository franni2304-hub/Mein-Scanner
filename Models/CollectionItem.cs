
using System;
using OnePieceCardScanner.Enums;

namespace OnePieceCardScanner.Models;

public sealed class CollectionItem
{
    public int Id { get; set; }

    // Eindeutige ID dieses physischen Exemplars
    public string InventoryId { get; set; } = string.Empty;

    // Verweis auf die konkrete Kartenvariante,
    // zum Beispiel OP05-060-JPN-AA
    public string CardDataId { get; set; } = string.Empty;

    public CardCondition Condition { get; set; } = CardCondition.NM;

    public InventoryStatus Status { get; set; } = InventoryStatus.STORED;

    // Lagerung, zum Beispiel BC-001 und Slot 218
    public string? ContainerCode { get; set; }

    public int? SlotNumber { get; set; }

    public string? EbayListingId { get; set; }

    public string? EbayOrderId { get; set; }

    public decimal? PurchasePrice { get; set; }

    public decimal? SalePrice { get; set; }

    public string? OriginalScanPath { get; set; }
    public int Test123 { get; set; }

    public string? ProcessedScanPath { get; set; }

    public string? ThumbnailPath { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.Now;

    public DateTime? SoldAt { get; set; }

    public string? GetStorageCode()
    {
        if (string.IsNullOrWhiteSpace(ContainerCode) ||
            SlotNumber is null)
        {
            return null;
        }

        return $"{ContainerCode}-{SlotNumber.Value:0000}";
    }
}