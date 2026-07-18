using OnePieceCardScanner.Recognition.Segmentation;
using OnePieceCardScanner.Recognition.TemplateMatching;
using System.Text;

namespace OnePieceCardScanner.Recognition.OCR;

public sealed class CardNumberRecognizer
{
    private readonly CharacterMatcher _matcher;
    private readonly CharacterRecognizer _recognizer;

    public CardNumberRecognizer(
        CharacterMatcher matcher)
    {
        _matcher = matcher;
        _recognizer = new CharacterRecognizer();
    }

    public string Recognize(
        IReadOnlyList<CharacterSegment> segments)
    {
        StringBuilder builder = new();

        foreach (CharacterSegment segment in segments)
        {
            var matches =
                _matcher.Match(segment.Image);

            char character =
                _recognizer.Recognize(matches);

            builder.Append(character);
        }

        return builder.ToString();
    }
}