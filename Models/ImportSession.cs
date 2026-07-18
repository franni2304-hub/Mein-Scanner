using System;

namespace OnePieceCardScanner.Models;

public sealed class ImportSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.Now;

    public DateTime? FinishedAt { get; set; }

    public int TotalImages { get; set; }

    public int RecognizedCards { get; set; }

    public int ManualReviewCards { get; set; }

    public int FailedCards { get; set; }

    public decimal? TotalPurchasePrice { get; set; }

    public string Currency { get; set; } = "EUR";

    public string? Source { get; set; }

    public string? Notes { get; set; }

    public string SourceType { get; set; } = "OTHER";

    public bool IsFinished => FinishedAt.HasValue;
}