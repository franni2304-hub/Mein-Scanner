using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Services;

public sealed class LocalCardDatabaseService
{
    private readonly Dictionary<string, List<LocalCardData>>
        _cardsByExactId =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly List<LocalCardData> _allCards = [];

    public int Count => _allCards.Count;

    public void Load()
    {
        _cardsByExactId.Clear();
        _allCards.Clear();

        string solutionFolder =
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    @"..\..\..\.."));

        string cardsFolder =
            Path.Combine(
                solutionFolder,
                "Cards");

        string jsonFolder =
            Path.Combine(
                cardsFolder,
                "Json");

        string imageFolder =
            Path.Combine(
                cardsFolder,
                "Images");

        if (!Directory.Exists(jsonFolder))
        {
            throw new DirectoryNotFoundException(
                $"JSON-Ordner wurde nicht gefunden:\n{jsonFolder}");
        }

        Dictionary<string, string> imagePaths =
            BuildImageIndex(imageFolder);

        JsonSerializerOptions options =
            new()
            {
                PropertyNameCaseInsensitive = true
            };

        string[] jsonFiles =
            Directory.GetFiles(
                jsonFolder,
                "*.json",
                SearchOption.AllDirectories);

        foreach (string jsonFile in jsonFiles)
        {
            try
            {
                string json =
                    File.ReadAllText(jsonFile);

                List<LocalCardData> cardsInFile =
                    DeserializeCards(json, options);

                foreach (LocalCardData card in cardsInFile)
                {
                    if (string.IsNullOrWhiteSpace(card.Id))
                    {
                        continue;
                    }

                    card.JsonFilePath = jsonFile;

                    if (imagePaths.TryGetValue(
                            card.Id,
                            out string? imagePath))
                    {
                        card.LocalImagePath =
                            imagePath;
                    }

                    _allCards.Add(card);

                    if (!_cardsByExactId.TryGetValue(
                            card.Id,
                            out List<LocalCardData>? exactCards))
                    {
                        exactCards = [];

                        _cardsByExactId.Add(
                            card.Id,
                            exactCards);
                    }

                    exactCards.Add(card);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"{jsonFile}\n{exception}");
            }
        }
    }

    public IReadOnlyList<LocalCardData> FindById(
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return [];
        }

        if (_cardsByExactId.TryGetValue(
                id,
                out List<LocalCardData>? cards))
        {
            return cards;
        }

        return [];
    }

    public IReadOnlyList<LocalCardData> FindVariants(
        string? printedCardNumber)
    {
        if (string.IsNullOrWhiteSpace(printedCardNumber))
        {
            return [];
        }

        string normalizedNumber =
            GetPrintedCardNumber(
                printedCardNumber);

        return _allCards
            .Where(card =>
                string.Equals(
                    GetPrintedCardNumber(card.Id),
                    normalizedNumber,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<LocalCardData> GetAllCards()
    {
        return _allCards;
    }

    public static string GetPrintedCardNumber(
        string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        int suffixIndex =
            id.IndexOf('_');

        return suffixIndex < 0
            ? id
            : id[..suffixIndex];
    }

    private static Dictionary<string, string>
        BuildImageIndex(
            string imageFolder)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(imageFolder))
        {
            return result;
        }

        string[] supportedExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".webp"
        };

        foreach (string imagePath in Directory.GetFiles(
                     imageFolder,
                     "*.*",
                     SearchOption.AllDirectories))
        {
            string extension =
                Path.GetExtension(imagePath);

            if (!supportedExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string id =
                Path.GetFileNameWithoutExtension(
                    imagePath);

            result.TryAdd(
                id,
                imagePath);
        }

        return result;
    }

    private static List<LocalCardData> DeserializeCards(
        string json,
        JsonSerializerOptions options)
    {
        using JsonDocument document =
            JsonDocument.Parse(json);

        if (document.RootElement.ValueKind ==
            JsonValueKind.Array)
        {
            return JsonSerializer
                       .Deserialize<List<LocalCardData>>(
                           json,
                           options)
                   ?? [];
        }

        if (document.RootElement.ValueKind ==
            JsonValueKind.Object)
        {
            LocalCardData? card =
                JsonSerializer.Deserialize<LocalCardData>(
                    json,
                    options);

            return card == null
                ? []
                : [card];
        }

        return [];
    }
}