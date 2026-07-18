using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Services;

public class CardDatabaseService
{
    private readonly List<CardData> _cards = new();

    public IReadOnlyList<CardData> Cards => _cards;

    public CardDatabaseService()
    {
        LoadCards();
    }

    private void LoadCards()
    {
        string filePath = Path.Combine(
            AppContext.BaseDirectory,
            "cards.json");

        if (!File.Exists(filePath))
        {
            return;
        }

        string json = File.ReadAllText(filePath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        List<CardData>? cards =
            JsonSerializer.Deserialize<List<CardData>>(
                json,
                options);

        if (cards != null)
        {
            _cards.AddRange(cards);
        }
    }

    public CardData? FindByCardNumber(string cardNumber)
    {
        return _cards.FirstOrDefault(card =>
            card.CardNumber.Equals(
                cardNumber,
                StringComparison.OrdinalIgnoreCase));
    }

    public CardData? FindById(string id)
    {
        return _cards.FirstOrDefault(card =>
            card.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<CardData> FindAllByCardNumber(string cardNumber)
    {
        return _cards
            .Where(card =>
                card.CardNumber.Equals(
                    cardNumber,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}