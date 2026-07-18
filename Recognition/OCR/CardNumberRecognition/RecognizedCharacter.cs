using System;
using System.Collections.Generic;
using System.Text;
using OnePieceCardScanner.Recognition.TemplateMatching;

namespace OnePieceCardScanner.Recognition.OCR.CardNumberRecognition;

public sealed class RecognizedCharacter
{
    public int Position { get; set; }

    public IReadOnlyList<CharacterMatch> Matches { get; set; }
        = [];

    public CharacterMatch Best =>
        Matches[0];
}