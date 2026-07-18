using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Models;

public sealed class StoragePosition
{
    public string ContainerCode { get; set; } = string.Empty;

    public int SlotNumber { get; set; }

    public string StorageCode =>
        $"{ContainerCode}-{SlotNumber:0000}";
}