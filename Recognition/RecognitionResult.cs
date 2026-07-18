namespace OnePieceCardScanner.Recognition;

public sealed class RecognitionResult
{
    public string CardNumber { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Rarity { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public bool HasStar { get; set; }

    public double Confidence { get; set; }
}