using OnePieceCardScanner.Models;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace OnePieceCardScanner.Services;

public sealed class PunkRecordsService
{
    private readonly List<PunkRecordCard> _cards = new();
    private readonly string _cardsFolder;

    public IReadOnlyList<PunkRecordCard> GetAllCards()
    {
        return _cards;
    }

    public int CountByCardNumber(string cardNumber)
    {
        return FindByCardNumber(cardNumber).Count;
    }

    public PunkRecordCard? FindById(string id)
    {
        return _cards.FirstOrDefault(card =>
            card.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));
    }

    public List<PunkRecordCard> FindByCardNumber(
    string cardNumber)
    {
        return _cards
            .Where(card =>
                card.Id.StartsWith(
                    cardNumber,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    public PunkRecordsService()
    {
        _cardsFolder = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "PunkRecords",
            "english",
            "cards");

        LoadAllCards();
    }
    public bool CardsFolderExists()
    {
        return Directory.Exists(_cardsFolder);
    }
    public PunkRecordCard? LoadFirstCard()
    {
        string[] files =
            Directory.GetFiles(
                _cardsFolder,
                "*.json",
                SearchOption.AllDirectories);

        if (files.Length == 0)
            return null;

        string json =
            File.ReadAllText(files[0]);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        return JsonSerializer.Deserialize<PunkRecordCard>(
            json,
            options);
    }
    private void LoadAllCards()
    {
        string[] files = Directory.GetFiles(
            _cardsFolder,
            "*.json",
            SearchOption.AllDirectories);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        foreach (string file in files)
        {
            try
            {
                string json = File.ReadAllText(file);

                PunkRecordCard? card =
                    JsonSerializer.Deserialize<PunkRecordCard>(
                        json,
                        options);

                if (card != null)
                {
                    _cards.Add(card);
                }
            }
            catch
            {
                // Fehlerhafte Datei überspringen
            }
        }
    }
}