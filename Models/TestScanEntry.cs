using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Models;

public sealed class TestScanEntry
{
    public string File { get; set; } = string.Empty;

    public string CardNumber { get; set; } = string.Empty;

    public string? Rarity { get; set; }

    public string? Language { get; set; }
}