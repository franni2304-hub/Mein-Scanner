using System;
using System.Collections.Generic;
using System.Text;
using OpenCvSharp;

namespace OnePieceCardScanner.Recognition;

public sealed class OrbDescriptor
{
    public char Character { get; set; }

    public string SourceImage { get; set; } =
        string.Empty;

    public KeyPoint[] KeyPoints { get; set; } =
        [];

    public Mat Descriptors { get; set; } =
        new();

    public int Count =>
        KeyPoints.Length;
}