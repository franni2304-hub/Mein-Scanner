using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceCardScanner.Recognition.Segmentation;

/// <summary>
/// Eine alternative geometrische Zerlegung desselben OCR-Ausschnitts.
/// Die enthaltenen Mat-Instanzen gehören dieser Hypothese und werden
/// durch Dispose freigegeben.
/// </summary>
public sealed class SegmentationHypothesis : IDisposable
{
    private bool _disposed;

    public SegmentationHypothesis(
        string sourceName,
        double geometryScore,
        IReadOnlyList<CharacterSegment> segments)
    {
        SourceName =
            sourceName ??
            string.Empty;

        GeometryScore =
            geometryScore;

        Segments =
            segments ??
            throw new ArgumentNullException(
                nameof(segments));
    }

    public string SourceName { get; }

    public double GeometryScore { get; }

    public IReadOnlyList<CharacterSegment> Segments { get; }

    public int CharacterCount =>
        Segments.Count;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (CharacterSegment segment in
                 Segments)
        {
            segment.Image?.Dispose();
        }

        _disposed =
            true;
    }

    public override string ToString()
    {
        return
            $"{SourceName}: {CharacterCount} Segmente, " +
            $"Geometrie={GeometryScore:F2}";
    }
}
