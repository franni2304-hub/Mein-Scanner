using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.ViewModels;

public sealed class InventoryItemViewModel
{
    public string InventoryId { get; set; } = string.Empty;

    public string CardNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public string Condition { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string StorageCode { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;
}