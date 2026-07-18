using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;


namespace OnePieceCardScanner.Recognition.Segmentation;

public sealed class CharacterSegment
{
    public int Position { get; set; }

    public Rect Bounds { get; set; }

    public Mat Image { get; set; } =
        new();

    public int Width =>
        Bounds.Width;

    public int Height =>
        Bounds.Height;

    public override string ToString()
    {
        return
            $"#{Position} ({Bounds.X}, {Bounds.Y}) " +
            $"{Bounds.Width}x{Bounds.Height}";
    }
}