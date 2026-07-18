using System;
using System.IO;
using System.Linq;
using System.Text;
using OnePieceCardScanner.Models;
using System.Collections.Generic;

namespace OnePieceCardScanner.Services;

public sealed class DatabaseAnalysisService
{
    public void Analyze()
    {
        IReadOnlyList<LocalCardData> cards =
            ServiceLocator.Database.GetAllCards();

        StringBuilder report = new();

        report.AppendLine(
            "===== ONE PIECE DATABASE ANALYSIS =====");

        report.AppendLine();

        report.AppendLine(
            $"Gesamtkarten: {cards.Count}");

        int uniqueCardNumbers =
            cards
                .Where(card =>
                    !string.IsNullOrWhiteSpace(card.Id))
                .Select(card => card.Id)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        report.AppendLine(
            $"Eindeutige Kartennummern: {uniqueCardNumbers}");

        report.AppendLine();

        var duplicateGroups =
            cards
                .Where(card =>
                    !string.IsNullOrWhiteSpace(card.Id))
                .GroupBy(
                    card => card.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Where(group =>
                    group.Count() > 1)
                .OrderBy(group =>
                    group.Key)
                .ToList();

        report.AppendLine(
            $"Mehrfach vorhandene Kartennummern: " +
            $"{duplicateGroups.Count}");

        report.AppendLine();

        var groupSizeSummary =
            duplicateGroups
                .GroupBy(group =>
                    group.Count())
                .OrderBy(group =>
                    group.Key);

        report.AppendLine(
            "Variantenverteilung:");

        foreach (var sizeGroup in groupSizeSummary)
        {
            report.AppendLine(
                $"  {sizeGroup.Key} Varianten: " +
                $"{sizeGroup.Count()} Kartennummern");
        }

        report.AppendLine();

        foreach (var group in duplicateGroups)
        {
            report.AppendLine(
                "========================================");

            report.AppendLine(
                $"{group.Key} ({group.Count()} Varianten)");

            report.AppendLine();

            foreach (LocalCardData card in group)
            {
                report.AppendLine(
                    $"  Name       : {card.Name}");

                report.AppendLine(
                    $"  Rarity     : {card.Rarity}");

                report.AppendLine(
                    $"  Category   : {card.Category}");

                report.AppendLine(
                    $"  Attributes : " +
                    $"{FormatValues(card.Attributes)}");

                report.AppendLine(
                    $"  Colors     : " +
                    $"{FormatValues(card.Colors)}");

                report.AppendLine(
                    $"  Types      : " +
                    $"{FormatValues(card.Types)}");

                report.AppendLine(
                    $"  Pack-ID    : {card.PackId}");

                report.AppendLine(
                    $"  JSON       : " +
                    $"{FormatFilePath(card.JsonFilePath)}");

                report.AppendLine(
                    $"  Image      : " +
                    $"{FormatFilePath(card.LocalImagePath)}");

                report.AppendLine();
            }
        }

        string reportPath =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Desktop),
                "DatabaseAnalysis.txt");

        File.WriteAllText(
            reportPath,
            report.ToString());

        System.Windows.MessageBox.Show(
            $"Analyse abgeschlossen.\n\n{reportPath}",
            "Datenbankanalyse");
    }

    private static string FormatValues(
        IEnumerable<string>? values)
    {
        if (values == null)
        {
            return "-";
        }

        string[] nonEmptyValues =
            values
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .ToArray();

        return nonEmptyValues.Length == 0
            ? "-"
            : string.Join(", ", nonEmptyValues);
    }

    private static string FormatFilePath(
        string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "-";
        }

        return Path.GetFileName(filePath);
    }
}
