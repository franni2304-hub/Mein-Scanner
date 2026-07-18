using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Recognition.TemplateMatching;

public sealed class CharacterMatch
{
    public char Character { get; init; }

    /// <summary>
    /// Gesamtbewertung von 0 bis 100.
    /// </summary>
    public double Score { get; init; }

    public double BestTemplateScore { get; init; }

    public int ComparedTemplateCount { get; init; }

    public CharacterTemplate BestTemplate { get; init; } =
        null!;

    public override string ToString()
    {
        return
            $"{Character}  {Score:0.0} %";
    }
}