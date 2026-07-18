using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OnePieceCardScanner.Models;
using OnePieceCardScanner.Recognition;

namespace OnePieceCardScanner.Services;

public sealed class OcrTemplatePreviewResult
{
    public string OutputFolder { get; init; } =
        string.Empty;

    public int ProcessedCards { get; init; }

    public int CreatedRawImages { get; init; }

    public int CreatedPreprocessedImages { get; init; }

    public int SkippedCards { get; init; }

    public int FailedCards { get; init; }
}

public sealed class OcrTemplatePreviewGenerator
{
    public OcrTemplatePreviewResult Generate(
        int maxCards = 300)
    {
        if (maxCards <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCards),
                "Die Anzahl der Karten muss größer als 0 sein.");
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
                "OCRTemplatePreview");

        string rawFolder =
            Path.Combine(
                outputFolder,
                "Raw");

        string preprocessedFolder =
            Path.Combine(
                outputFolder,
                "Preprocessed");

        Directory.CreateDirectory(
            rawFolder);

        Directory.CreateDirectory(
            preprocessedFolder);

        IReadOnlyList<LocalCardData> allCards =
            ServiceLocator.Database.GetAllCards();

        List<LocalCardData> cards =
            allCards
                .Where(card =>
                    !string.IsNullOrWhiteSpace(card.Id))
                .GroupBy(
                    card => card.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .Take(maxCards)
                .ToList();

        int processedCards = 0;
        int createdRawImages = 0;
        int createdPreprocessedImages = 0;
        int skippedCards = 0;
        int failedCards = 0;

        var csv =
            new StringBuilder();

        csv.AppendLine(
            "ExactId;PrintedNumber;CandidateIndex;RawFile;PreprocessedFile");

        var errors =
            new StringBuilder();

        foreach (LocalCardData card in cards)
        {
            if (string.IsNullOrWhiteSpace(
                    card.LocalImagePath) ||
                !File.Exists(
                    card.LocalImagePath))
            {
                skippedCards++;
                continue;
            }

            try
            {
                string printedNumber =
                    LocalCardDatabaseService
                        .GetPrintedCardNumber(
                            card.Id);

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

                    string safeId =
                        MakeSafeFileName(
                            card.Id);

                    string rawFileName =
                        $"{safeId}_c{candidateIndex}.png";

                    string rawDestination =
                        Path.Combine(
                            rawFolder,
                            rawFileName);

                    File.Copy(
                        candidatePath,
                        rawDestination,
                        overwrite: true);

                    createdRawImages++;

                    string preparedPath =
                        CardImagePreprocessor.Prepare(
                            candidatePath);

                    string preprocessedFileName =
                        $"{safeId}_c{candidateIndex}.png";

                    string preprocessedDestination =
                        Path.Combine(
                            preprocessedFolder,
                            preprocessedFileName);

                    File.Copy(
                        preparedPath,
                        preprocessedDestination,
                        overwrite: true);

                    createdPreprocessedImages++;

                    csv.AppendLine(
                        string.Join(
                            ";",
                            EscapeCsv(card.Id),
                            EscapeCsv(printedNumber),
                            candidateIndex.ToString(),
                            EscapeCsv(
                                Path.Combine(
                                    "Raw",
                                    rawFileName)),
                            EscapeCsv(
                                Path.Combine(
                                    "Preprocessed",
                                    preprocessedFileName))));
                }

                processedCards++;
            }
            catch (Exception exception)
            {
                failedCards++;

                errors.AppendLine(
                    $"{card.Id}: {exception.Message}");
            }
        }

        File.WriteAllText(
            Path.Combine(
                outputFolder,
                "preview-index.csv"),
            csv.ToString(),
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(
                outputFolder,
                "errors.txt"),
            errors.ToString(),
            Encoding.UTF8);

        string summary =
            $"Verarbeitete Karten: {processedCards}{Environment.NewLine}" +
            $"Raw-Ausschnitte: {createdRawImages}{Environment.NewLine}" +
            $"Vorverarbeitete Ausschnitte: {createdPreprocessedImages}{Environment.NewLine}" +
            $"Übersprungene Karten ohne lokales Bild: {skippedCards}{Environment.NewLine}" +
            $"Fehler: {failedCards}{Environment.NewLine}" +
            $"Ausgabeordner: {outputFolder}";

        File.WriteAllText(
            Path.Combine(
                outputFolder,
                "summary.txt"),
            summary,
            Encoding.UTF8);

        return new OcrTemplatePreviewResult
        {
            OutputFolder =
                outputFolder,

            ProcessedCards =
                processedCards,

            CreatedRawImages =
                createdRawImages,

            CreatedPreprocessedImages =
                createdPreprocessedImages,

            SkippedCards =
                skippedCards,

            FailedCards =
                failedCards
        };
    }

    private static string MakeSafeFileName(
        string value)
    {
        char[] invalidCharacters =
            Path.GetInvalidFileNameChars();

        var result =
            new StringBuilder(
                value.Length);

        foreach (char character in value)
        {
            result.Append(
                invalidCharacters.Contains(character)
                    ? '_'
                    : character);
        }

        return result.ToString();
    }

    private static string EscapeCsv(
        string value)
    {
        string escaped =
            value.Replace(
                "\"",
                "\"\"");

        return $"\"{escaped}\"";
    }
}
