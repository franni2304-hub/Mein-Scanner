using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace OnePieceCardScanner.Models;

public sealed class LocalCardData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("colors")]
    public List<string> Colors { get; set; } = [];

    [JsonPropertyName("types")]
    public List<string> Types { get; set; } = [];

    [JsonPropertyName("attributes")]
    public List<string> Attributes { get; set; } = [];

    [JsonPropertyName("cost")]
    public int? Cost { get; set; }

    [JsonPropertyName("power")]
    public int? Power { get; set; }

    [JsonPropertyName("counter")]
    public int? Counter { get; set; }

    [JsonPropertyName("effect")]
    public string? Effect { get; set; }

    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    [JsonPropertyName("pack_id")]
    public string PackId { get; set; } = string.Empty;

    [JsonPropertyName("img_url")]
    public string ImageUrl { get; set; } = string.Empty;

    [JsonPropertyName("img_full_url")]
    public string FullImageUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public string JsonFilePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string LocalImagePath { get; set; } = string.Empty;
}