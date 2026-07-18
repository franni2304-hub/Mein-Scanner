namespace OnePieceCardScanner.Models;

public sealed class CardData
{
    public string Id { get; set; } = string.Empty;

    public string CardNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string SetCode { get; set; } = string.Empty;

    public string Rarity { get; set; } = string.Empty;

    public string CardType { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public string ArtworkVariant { get; set; } = string.Empty;

    public string Finish { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;
}