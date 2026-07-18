using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Models;

public sealed class CardInfo
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Rarity { get; set; } = "";

    public string Category { get; set; } = "";

    public string[] Colors { get; set; } = [];

    public string[] Types { get; set; } = [];

    public int? Cost { get; set; }

    public int? Power { get; set; }

    public int? Counter { get; set; }

    public string Effect { get; set; } = "";

    public string Trigger { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public string JsonPath { get; set; } = "";
}