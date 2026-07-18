using Microsoft.Win32;
using OnePieceCardScanner.Recognition.OCR.Debugging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OnePieceCardScanner.Recognition.OCR.Core;

namespace OnePieceCardScanner.Views;

public partial class OcrDebugExplorerWindow : System.Windows.Window
{
    private readonly OcrDebugPipeline _pipeline = new();
    private OcrDebugResult? _currentResult;

    public OcrDebugExplorerWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _pipeline.Dispose();
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bilddateien|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Alle Dateien|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        ImagePathTextBox.Text = dialog.FileName;
        AnalyzeButton.IsEnabled = true;
        StatusText.Text = "Bild ausgewählt. Klicke auf Analysieren.";
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        string imagePath = ImagePathTextBox.Text;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(
                "Bitte zuerst ein gültiges Bild auswählen.",
                "OCR Debug Explorer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            AnalyzeButton.IsEnabled = false;
            StatusText.Text = "Analyse läuft …";

            _currentResult = await Task.Run(() => _pipeline.Analyze(imagePath));
            OriginalImage.Source = ToBitmapSource(_currentResult.OriginalImagePng);

            List<RegionViewItem> regionItems = _currentResult.Regions
                .Select(region => new RegionViewItem
                {
                    Region = region,
                    DisplayText =
                        $"Region {region.Index}: {region.SegmentCount} Segmente | " +
                        $"{region.GreedyText} | {region.AverageScore:0.0} %"
                })
                .ToList();

            RegionComboBox.ItemsSource = regionItems;

            int selectedIndex = regionItems.FindIndex(item =>
                item.Region.Index == _currentResult.BestRegionIndex);

            RegionComboBox.SelectedIndex = selectedIndex >= 0
                ? selectedIndex
                : regionItems.Count > 0 ? 0 : -1;

            StatusText.Text =
                $"Analyse abgeschlossen. Erwartet: {_currentResult.ExpectedText}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "OCR Debug Explorer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Analyse fehlgeschlagen.";
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    private void RegionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RegionComboBox.SelectedItem is not RegionViewItem selected)
            return;

        OcrDebugRegionResult region = selected.Region;
        RegionImage.Source = ToBitmapSource(region.RegionImagePng);
        RegionSummaryText.Text =
            $"Greedy: {region.GreedyText}\n" +
            $"Segmente: {region.SegmentCount}\n" +
            $"Erwartete Länge: {region.ExpectedLength}\n" +
            $"Längendifferenz: {region.LengthDifference}\n" +
            $"Durchschnittlicher Score: {region.AverageScore:0.0} %";

        List<SegmentViewItem> segmentItems = region.Segments
            .Select(segment =>
            {
                OcrDebugMatchResult? best = segment.Matches.FirstOrDefault();
                return new SegmentViewItem
                {
                    Segment = segment,
                    Header = $"#{segment.Index + 1}",
                    Image = ToBitmapSource(segment.SegmentImagePng),
                    BestText = best == null ? "?" : $"{best.Character} {best.Score:0.0}%"
                };
            })
            .ToList();

        SegmentsList.ItemsSource = segmentItems;
        SegmentsList.SelectedIndex = segmentItems.Count > 0 ? 0 : -1;
    }

    private void SegmentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SegmentsList.SelectedItem is not SegmentViewItem selected)
            return;

        OcrDebugSegmentResult segment = selected.Segment;
        SelectedSegmentImage.Source = ToBitmapSource(segment.SegmentImagePng);
        SelectedSegmentText.Text =
            $"Segment #{segment.Index + 1}\n{segment.Matches.Count} Treffer geladen";

        List<MatchViewItem> matchItems = segment.Matches
            .Select((match, index) => new MatchViewItem
            {
                Match = match,
                Rank = $"{index + 1}.",
                CharacterText = match.Character.ToString(),
                ScoreText =
                    $"{match.Score:0.0} % (Template {match.BestTemplateScore:0.0} %)",
                FileName = Path.GetFileName(match.TemplateFilePath)
            })
            .ToList();

        MatchesList.ItemsSource = matchItems;
        MatchesList.SelectedIndex = matchItems.Count > 0 ? 0 : -1;
    }

    private void MatchesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MatchesList.SelectedItem is not MatchViewItem selected)
        {
            BestTemplateImage.Source = null;
            return;
        }

        BestTemplateImage.Source = ToBitmapSource(selected.Match.TemplateImagePng);
    }

    private static BitmapSource? ToBitmapSource(byte[] bytes)
    {
        if (bytes.Length == 0)
            return null;

        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private sealed class RegionViewItem
    {
        public string DisplayText { get; init; } = string.Empty;
        public OcrDebugRegionResult Region { get; init; } = null!;
    }

    private sealed class SegmentViewItem
    {
        public string Header { get; init; } = string.Empty;
        public BitmapSource? Image { get; init; }
        public string BestText { get; init; } = string.Empty;
        public OcrDebugSegmentResult Segment { get; init; } = null!;
    }

    private sealed class MatchViewItem
    {
        public string Rank { get; init; } = string.Empty;
        public string CharacterText { get; init; } = string.Empty;
        public string ScoreText { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public OcrDebugMatchResult Match { get; init; } = null!;
    }
}