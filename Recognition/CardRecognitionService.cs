using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OnePieceCardScanner.Models;
using OnePieceCardScanner.Recognition.OCR;
using OnePieceCardScanner.Services;

namespace OnePieceCardScanner.Recognition;

public class CardRecognitionService : ICardRecognitionService
{
    private readonly CardVariantResolver _variantResolver =
        new();

    private readonly CardNumberRecognitionEngine _numberEngine =
        new();

    public RecognitionResult Recognize(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException(
                "Es wurde kein Bildpfad übergeben.",
                nameof(imagePath));
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException(
                "Das ausgewählte Bild wurde nicht gefunden.",
                imagePath);
        }

        OcrDebugService.CreateSession();

        OcrDebugService.SaveFile(
            imagePath,
            "00_original.png");

        string detectedCardPath =
            CardDetector.DetectAndCropCard(
                imagePath);

        OcrDebugService.SaveFile(
            detectedCardPath,
            "01_detected_card.png");

        bool hasStar =
            TryDetectStar(
                detectedCardPath);

        CardNumberRecognitionResult numberResult =
            _numberEngine.RecognizeCard(
                detectedCardPath);

        OcrDebugService.SaveText(
            "number_engine_result.txt",
            $"CardNumber: {numberResult.CardNumber}\n" +
            $"Greedy: {numberResult.GreedyText}\n" +
            $"Confidence: {numberResult.Confidence:0.0}\n" +
            $"ImageScore: {numberResult.ImageScore:0.0}\n" +
            $"Segments: {numberResult.SegmentCount}");

        if (!numberResult.Success)
        {
            return new RecognitionResult
            {
                CardNumber = string.Empty,
                Language = "ENG",
                Rarity = "UNBEKANNT",
                Variant = hasStar
                    ? "Stern erkannt, Karte unbekannt"
                    : "UNBEKANNT",
                HasStar = hasStar,
                Confidence = 0
            };
        }

        string printedCardNumber =
            numberResult.CardNumber;

        IReadOnlyList<LocalCardData> variants =
            ServiceLocator.Database.FindVariants(
                printedCardNumber);

        if (variants.Count == 0)
        {
            return new RecognitionResult
            {
                CardNumber = printedCardNumber,
                Language = "ENG",
                Rarity = "UNBEKANNT",
                Variant = "Kartennummer erkannt, aber keine Variante gefunden",
                HasStar = hasStar,
                Confidence = numberResult.Confidence
            };
        }

        if (variants.Count == 1)
        {
            return CreateResult(
                variants[0],
                printedCardNumber,
                hasStar,
                confidence: numberResult.Confidence,
                variantWasResolved: true);
        }

        LocalCardData? resolvedCard =
            _variantResolver.Resolve(
                detectedCardPath,
                variants);

        if (resolvedCard != null)
        {
            return CreateResult(
                resolvedCard,
                printedCardNumber,
                hasStar,
                confidence: Math.Min(
                    100,
                    numberResult.Confidence),
                variantWasResolved: true);
        }

        return new RecognitionResult
        {
            CardNumber = printedCardNumber,
            Language = "ENG",
            Rarity = GetSharedRarity(
                variants),
            Variant =
                $"{variants.Count} Varianten – nicht eindeutig",
            HasStar = hasStar,
            Confidence = Math.Min(
                85,
                numberResult.Confidence)
        };

        return new RecognitionResult
        {
            CardNumber = string.Empty,
            Language = "ENG",
            Rarity = "UNBEKANNT",
            Variant = hasStar
        ? "Stern erkannt, Karte unbekannt"
        : "UNBEKANNT",
            HasStar = hasStar,
            Confidence = 0
        };
    }

    private static RecognitionResult CreateResult(
        LocalCardData card,
        string printedCardNumber,
        bool hasStar,
        double confidence,
        bool variantWasResolved)
    {
        string variantLabel =
            GetVariantLabel(
                card.Id);

        if (hasStar &&
            string.Equals(
                variantLabel,
                "Standard",
                StringComparison.OrdinalIgnoreCase))
        {
            variantLabel =
                "Parallel / Alt Art ⭐";
        }
        else if (hasStar)
        {
            variantLabel += " ⭐";
        }

        return new RecognitionResult
        {
            CardNumber = variantWasResolved
                ? card.Id
                : printedCardNumber,

            Language = "ENG",

            Rarity = string.IsNullOrWhiteSpace(
                card.Rarity)
                    ? "UNBEKANNT"
                    : card.Rarity,

            Variant = variantLabel,

            HasStar = hasStar,

            Confidence = confidence
        };
    }

    private static bool TryDetectStar(
        string detectedCardPath)
    {
        try
        {
            return StarDetector.Detect(
                detectedCardPath);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Sternprüfung fehlgeschlagen:\n{exception}");

            return false;
        }
    }

    private static string GetSharedRarity(
        IReadOnlyList<LocalCardData> variants)
    {
        string? rarity = null;

        foreach (LocalCardData card in variants)
        {
            if (string.IsNullOrWhiteSpace(
                    card.Rarity))
            {
                continue;
            }

            if (rarity == null)
            {
                rarity = card.Rarity;
                continue;
            }

            if (!string.Equals(
                    rarity,
                    card.Rarity,
                    StringComparison.OrdinalIgnoreCase))
            {
                return "UNBEKANNT";
            }
        }

        return rarity ?? "UNBEKANNT";
    }

    private static string GetVariantLabel(
        string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return "UNBEKANNT";
        }

        int suffixIndex =
            cardId.IndexOf('_');

        if (suffixIndex < 0)
        {
            return "Standard";
        }

        string suffix =
            cardId[(suffixIndex + 1)..]
                .ToUpperInvariant();

        return suffix switch
        {
            "P1" => "Parallel / Alt Art P1",
            "P2" => "Parallel / Alt Art P2",
            "P3" => "Parallel / Alt Art P3",
            "P4" => "Parallel / Alt Art P4",
            _ => suffix
        };
    }

    private static void SaveSuccessfulNumberRegion(
        string candidatePath)
    {
        string successfulRegionPath =
            Path.Combine(
                Path.GetTempPath(),
                "onepiece-card-number-region.png");

        File.Copy(
            candidatePath,
            successfulRegionPath,
            overwrite: true);
    }

    private static string? ExtractCardNumber(
        string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        string text = rawText
            .ToUpperInvariant()
            .Replace("_", "-")
            .Replace("—", "-")
            .Replace("–", "-")
            .Replace("0P", "OP");

        Match match = Regex.Match(
            text,
            @"(?<prefix>OP|ST|EB|PRB)\s*" +
            @"(?<set>[0-9OILBSZ]{2})\s*" +
            @"-?\s*" +
            @"(?<number>[0-9OILBSZ]{3})");

        if (match.Success)
        {
            string prefix =
                match.Groups["prefix"].Value;

            string setNumber =
                NormalizeDigits(
                    match.Groups["set"].Value);

            string cardNumber =
                NormalizeDigits(
                    match.Groups["number"].Value);

            return $"{prefix}{setNumber}-{cardNumber}";
        }

        match = Regex.Match(
            text,
            @"(?<![A-Z0-9])P\s*-?\s*" +
            @"(?<number>[0-9OILBSZ]{3})");

        if (match.Success)
        {
            string cardNumber =
                NormalizeDigits(
                    match.Groups["number"].Value);

            return $"P-{cardNumber}";
        }

        return null;
    }

    private static string NormalizeDigits(
        string value)
    {
        return value
            .Replace('O', '0')
            .Replace('I', '1')
            .Replace('L', '1')
            .Replace('B', '8')
            .Replace('S', '5')
            .Replace('Z', '2');
    }
}
