using System;
using System.Collections.Generic;
using System.Text;
using System;
using System.Collections.Generic;
using System.IO;

namespace OnePieceCardScanner.Models;
public sealed class CardPrinting
{
    public int Id { get; set; }

    public string CardNumber { get; set; } = string.Empty;

    public string EnglishName { get; set; } = string.Empty;

    public string JapaneseName { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public string SetCode { get; set; } = string.Empty;

    public string? ReferenceImagePath { get; set; }
}