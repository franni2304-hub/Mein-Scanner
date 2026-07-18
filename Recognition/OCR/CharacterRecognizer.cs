using System.Linq;
using OnePieceCardScanner.Recognition.TemplateMatching;

namespace OnePieceCardScanner.Recognition.OCR;

public sealed class CharacterRecognizer
{
    public char Recognize(
        IReadOnlyList<CharacterMatch> matches)
    {
        if (matches.Count == 0)
        {
            return '?';
        }

        return matches
            .OrderByDescending(m => m.Score)
            .First()
            .Character;
    }
}