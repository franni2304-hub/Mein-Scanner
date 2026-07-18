using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Models;

public sealed class StorageLocation
{
    public int Id { get; set; }

    public string BoxCode { get; set; } = string.Empty;

    public int SlotNumber { get; set; }

    public bool IsOccupied { get; set; }

    public string? InventoryId { get; set; }
}