namespace OnePieceCardScanner.Recognition.OCR.Core;

public sealed class OcrCoreResult
{
    public string CardNumber { get; init; } =
        string.Empty;

    public string GreedyText { get; init; } =
        string.Empty;

    public double Confidence { get; init; }

    public double ImageScore { get; init; }

    public int SegmentCount { get; init; }

    public bool Success =>
        !string.IsNullOrWhiteSpace(
            CardNumber);
}