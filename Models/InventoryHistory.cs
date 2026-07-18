using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Models;

public sealed class InventoryHistory
{
    public int Id { get; set; }

    public string InventoryId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string Action { get; set; } = string.Empty;

    public string? User { get; set; }

    public string? Notes { get; set; }
}