using OnePieceCardScanner.Models;
using OnePieceCardScanner.Recognition;
using OnePieceCardScanner.Recognition.Segmentation;
using OpenCvSharp;
using System;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text;

namespace OnePieceCardScanner.Services;

public sealed class CharacterTemplateGenerationResult
{
    public string OutputFolder { get; init; } =
        string.Empty;

    public int ProcessedCards { get; init; }

    public int AcceptedCards { get; init; }

    public int RejectedCards { get; init; }

    public int CreatedTemplates { get; init; }

    public int SkippedCards { get; init; }
}

public sealed class CharacterTemplateGenerator
{
    private const int MaximumTemplatesPerCharacter = 200;

    private readonly CharacterSegmenter _segmenter =
        new();

    public CharacterTemplateGenerationResult Generate(
        int maximumCards = 500)
    {
        if (maximumCards <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCards));
        }

        string solutionFolder =
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    @"..\..\..\.."));

        string outputFolder =
            Path.Combine(
                solutionFolder,
                "Data",
                "OCRTemplates");

        string rejectedFolder =
            Path.Combine(
                outputFolder,
                "Rejected");

        Directory.CreateDirectory(
            outputFolder);

        Directory.CreateDirectory(
            rejectedFolder);

        Dictionary<char, int> templateCounts =
            ReadExistingTemplateCounts(
                outputFolder);

        List<LocalCardData> cards =
            ServiceLocator.Database
                .GetAllCards()
                .Where(card =>
                    !string.IsNullOrWhiteSpace(card.Id))
                .Where(card =>
                    !string.IsNullOrWhiteSpace(
                        card.LocalImagePath))
                .Where(card =>
                    File.Exists(card.LocalImagePath))
                .GroupBy(
                    card => card.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .Take(maximumCards)
                .ToList();

        int processedCards = 0;
        int acceptedCards = 0;
        int rejectedCards = 0;
        int createdTemplates = 0;
        int skippedCards = 0;

        var report =
            new StringBuilder();

        foreach (LocalCardData card in cards)
        {
            processedCards++;

            string printedNumber =
                LocalCardDatabaseService
                    .GetPrintedCardNumber(
                        card.Id);

            if (!IsSupportedCardNumber(
                    printedNumber))
            {
                skippedCards++;

                report.AppendLine(
                    $"{card.Id}: Nummernformat übersprungen.");

                continue;
            }

            try
            {
                bool accepted =
                    TryGenerateTemplates(
                        card,
                        printedNumber,
                        outputFolder,
                        rejectedFolder,
                        templateCounts,
                        out int templatesCreated,
                        out string status);

                createdTemplates +=
                    templatesCreated;

                if (accepted)
                {
                    acceptedCards++;
                }
                else
                {
                    rejectedCards++;
                }

                report.AppendLine(
                    $"{card.Id}: {status}");
            }
            catch (Exception exception)
            {
                rejectedCards++;

                report.AppendLine(
                    $"{card.Id}: FEHLER – " +
                    exception.Message);
            }
        }

        string summary =
            $"Verarbeitete Karten: {processedCards}" +
            Environment.NewLine +
            $"Akzeptierte Karten: {acceptedCards}" +
            Environment.NewLine +
            $"Abgelehnte Karten: {rejectedCards}" +
            Environment.NewLine +
            $"Übersprungene Karten: {skippedCards}" +
            Environment.NewLine +
            $"Erzeugte Templates: {createdTemplates}" +
            Environment.NewLine +
            $"Ausgabeordner: {outputFolder}";

        File.WriteAllText(
            Path.Combine(
                outputFolder,
                "generation-summary.txt"),
            summary);

        File.WriteAllText(
            Path.Combine(
                outputFolder,
                "generation-report.txt"),
            report.ToString());

        return new CharacterTemplateGenerationResult
        {
            OutputFolder = outputFolder,
            ProcessedCards = processedCards,
            AcceptedCards = acceptedCards,
            RejectedCards = rejectedCards,
            CreatedTemplates = createdTemplates,
            SkippedCards = skippedCards
        };
    }

    private bool TryGenerateTemplates(
        LocalCardData card,
        string printedNumber,
        string outputFolder,
        string rejectedFolder,
        Dictionary<char, int> templateCounts,
        out int templatesCreated,
        out string status)
    {
        templatesCreated = 0;

        IReadOnlyList<string> candidates =
            CardRegionExtractor
                .ExtractCardNumberCandidates(
                    card.LocalImagePath);

        for (int candidateIndex = 0;
             candidateIndex < candidates.Count;
             candidateIndex++)
        {
            string candidatePath =
                candidates[candidateIndex];

            string preparedPath =
                CardImagePreprocessor.Prepare(
                    candidatePath);

            IReadOnlyList<CharacterSegment> segments =
                _segmenter.Segment(
                    preparedPath);

            if (segments.Count !=
                printedNumber.Length)
            {
                DisposeSegments(
                    segments);

                continue;
            }

            for (int characterIndex = 0;
                 characterIndex < printedNumber.Length;
                 characterIndex++)
            {
                char character =
                    printedNumber[characterIndex];

                if (!CanStoreCharacter(
                        character,
                        templateCounts))
                {
                    continue;
                }

                string characterFolderName =
                    GetCharacterFolderName(
                        character);

                string characterFolder =
                    Path.Combine(
                        outputFolder,
                        characterFolderName);

                Directory.CreateDirectory(
                    characterFolder);

                string safeCardId =
                    MakeSafeFileName(
                        card.Id);

                string templatePath =
                    Path.Combine(
                        characterFolder,
                        $"{safeCardId}_" +
                        $"c{candidateIndex}_" +
                        $"p{characterIndex}.png");

                Cv2.ImWrite(
                    templatePath,
                    segments[characterIndex].Image);

                templateCounts[character] =
                    templateCounts.GetValueOrDefault(
                        character) + 1;

                templatesCreated++;
            }

            DisposeSegments(
                segments);

            status =
                $"Akzeptiert, Kandidat {candidateIndex}, " +
                $"{templatesCreated} Templates.";

            return true;
        }

        SaveRejectedPreview(
            card,
            candidates,
            rejectedFolder);

        status =
            "Abgelehnt: Kein Kandidat hatte " +
            $"{printedNumber.Length} Segmente.";

        return false;
    }

    private static bool CanStoreCharacter(
        char character,
        IReadOnlyDictionary<char, int> counts)
    {
        return counts.GetValueOrDefault(
            character) <
            MaximumTemplatesPerCharacter;
    }

    private static Dictionary<char, int>
        ReadExistingTemplateCounts(
            string outputFolder)
    {
        var result =
            new Dictionary<char, int>();

        foreach (string folder in
                 Directory.GetDirectories(
                     outputFolder))
        {
            string name =
                Path.GetFileName(folder);

            if (string.Equals(
                    name,
                    "Rejected",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            char? character =
                GetCharacterFromFolderName(
                    name);

            if (character == null)
            {
                continue;
            }

            result[character.Value] =
                Directory.GetFiles(
                    folder,
                    "*.png",
                    SearchOption.TopDirectoryOnly)
                .Length;
        }

        return result;
    }

    private static void SaveRejectedPreview(
        LocalCardData card,
        IReadOnlyList<string> candidates,
        string rejectedFolder)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        string safeId =
            MakeSafeFileName(
                card.Id);

        string destination =
            Path.Combine(
                rejectedFolder,
                $"{safeId}.png");

        File.Copy(
            candidates[0],
            destination,
            overwrite: true);
    }

    private static void DisposeSegments(
        IReadOnlyList<CharacterSegment> segments)
    {
        foreach (CharacterSegment segment in segments)
        {
            segment.Image.Dispose();
        }
    }

    private static bool IsSupportedCardNumber(
        string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return false;
        }

        return cardNumber.StartsWith(
                   "OP",
                   StringComparison.OrdinalIgnoreCase) ||
               cardNumber.StartsWith(
                   "ST",
                   StringComparison.OrdinalIgnoreCase) ||
               cardNumber.StartsWith(
                   "EB",
                   StringComparison.OrdinalIgnoreCase) ||
               cardNumber.StartsWith(
                   "PRB",
                   StringComparison.OrdinalIgnoreCase) ||
               cardNumber.StartsWith(
                   "P-",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCharacterFolderName(
        char character)
    {
        return character == '-'
            ? "Dash"
            : character.ToString();
    }

    private static char? GetCharacterFromFolderName(
        string folderName)
    {
        if (string.Equals(
                folderName,
                "Dash",
                StringComparison.OrdinalIgnoreCase))
        {
            return '-';
        }

        return folderName.Length == 1
            ? folderName[0]
            : null;
    }

    private static string MakeSafeFileName(
        string value)
    {
        char[] invalidCharacters =
            Path.GetInvalidFileNameChars();

        var result =
            new StringBuilder();

        foreach (char character in value)
        {
            result.Append(
                invalidCharacters.Contains(character)
                    ? '_'
                    : character);
        }

        return result.ToString();
    }
}