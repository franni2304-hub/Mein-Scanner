using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Models;

public sealed class PunkRecordCard
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public List<string> Colors { get; set; } = [];

    public int? Cost { get; set; }

    public int? Power { get; set; }

    public int? Counter { get; set; }

    public string Rarity { get; set; } = string.Empty;

    public string? Effect { get; set; }

    public string? Trigger { get; set; }

    public List<string> Attributes { get; set; } = [];

    public List<string> Types { get; set; } = [];

    public string ImgUrl { get; set; } = string.Empty;

    public string ImgFullUrl { get; set; } = string.Empty;

    public string PackId { get; set; } = string.Empty;
}