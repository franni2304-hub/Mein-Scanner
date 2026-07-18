using Microsoft.Win32;
using OnePieceCardScanner.Models;
using OnePieceCardScanner.Recognition;
using OnePieceCardScanner.Recognition.OCR;
using OnePieceCardScanner.Recognition.TemplateMatching;
using OnePieceCardScanner.Services;
using OnePieceCardScanner.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OnePieceCardScanner;

public partial class MainWindow : Window
{
    private string? _selectedImagePath;

    public MainWindow()
    {
        InitializeComponent();


        LoadLocalDatabase();
        ResetCardDetails();
        CharacterTemplateLibrary library =
        new();

        library.Load(
            @"C:\Users\Franni\source\repos\OnePieceCardScanner\Data\OCRTemplates");

        MessageBox.Show(
            $"Es wurden {library.GetAllTemplates().Count()} Templates geladen.");
    }
    private void OpenOcrDebugExplorer_Click(
    object sender,
    RoutedEventArgs e)
    {
        var window =
            new OcrDebugExplorerWindow
            {
                Owner = this
            };

        window.ShowDialog();
    }
    private void RunOcrBenchmark_Click(
    object sender,
    RoutedEventArgs e)
    {
        var window =
            new OcrBenchmarkProgressWindow
            {
                Owner = this
            };

        window.ShowDialog();
    }
    private void OpenOcrTemplateAnalyzer_Click(
    object sender,
    RoutedEventArgs e)
    {
        var window =
            new OcrTemplateAnalyzerWindow
            {
                Owner = this
            };

        window.ShowDialog();
    }

    private void OpenSegmentTraining_Click(
    object sender,
    RoutedEventArgs e)
    {
        var window =
            new SegmentTrainingWindow
            {
                Owner = this
            };

        window.ShowDialog();
    }

    private void TestCharacterSegmentation_Click(
    object sender,
    RoutedEventArgs e)
    {
        var dialog =
            new OpenFileDialog
            {
                Title =
                    "Vorverarbeitetes Nummernbild auswählen",

                Filter =
                    "PNG-Dateien|*.png|" +
                    "Alle Bilddateien|" +
                    "*.png;*.jpg;*.jpeg;*.bmp|" +
                    "Alle Dateien|*.*"
            };

        bool? dialogResult =
            dialog.ShowDialog();

        if (dialogResult != true)
        {
            return;
        }

        string expectedText =
            Microsoft.VisualBasic.Interaction.InputBox(
                "Welche Kartennummer ist auf dem Bild zu sehen?\n\n" +
                "Beispiele:\n" +
                "OP09-093\n" +
                "PRB01-001\n" +
                "P-001",
                "Erwartete Kartennummer",
                string.Empty);

        if (string.IsNullOrWhiteSpace(
                expectedText))
        {
            return;
        }

        try
        {
            var debugService =
                new CharacterSegmentationDebugService();

            CharacterSegmentationDebugResult result =
                debugService.Run(
                    dialog.FileName,
                    expectedText
                        .Trim()
                        .ToUpperInvariant());

            MessageBox.Show(
                $"Segmentierung abgeschlossen.\n\n" +
                $"Erwartete Zeichen: " +
                $"{result.ExpectedText.Length}\n" +
                $"Gefundene Segmente: " +
                $"{result.SegmentCount}\n" +
                $"Anzahl stimmt: " +
                $"{(result.CountMatches ? "Ja" : "Nein")}",
                "Segmentierungstest",
                MessageBoxButton.OK,
                result.CountMatches
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Fehler beim Segmentierungstest",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadLocalDatabase()
    {
        try
        {
            ServiceLocator.Database.Load();

            DatabaseStatusText.Text =
                $"{ServiceLocator.Database.Count} lokale Karten geladen";
        }
        catch (Exception exception)
        {
            DatabaseStatusText.Text =
                "Kartendatenbank konnte nicht geladen werden";

            MessageBox.Show(
                exception.ToString(),
                "Fehler beim Laden der Kartendatenbank",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RecognizeCard_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedImagePath))
        {
            MessageBox.Show(
                "Bitte zuerst ein Bild auswählen.");

            return;
        }

        RecognizeSelectedImage();
    }

    private void RecognizeSelectedImage()
    {
        if (string.IsNullOrWhiteSpace(_selectedImagePath))
        {
            return;
        }

        try
        {
            var recognitionService =
                new CardRecognitionService();

            RecognitionResult result =
                recognitionService.Recognize(
                    _selectedImagePath);

            ShowRecognitionResult(result);

            if (string.IsNullOrWhiteSpace(result.CardNumber))
            {
                MessageBox.Show(
                    "Es konnte keine Kartennummer erkannt werden.",
                    "OCR-Ergebnis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Fehler bei der Kartenerkennung",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowRecognitionResult(
        RecognitionResult result)
    {
        CardNumberText.Text =
            DisplayValue(result.CardNumber);

        LanguageText.Text =
            DisplayValue(result.Language);

        RarityText.Text =
            DisplayValue(result.Rarity);

        VariantText.Text =
            DisplayValue(result.Variant);

        ConfidenceText.Text =
            $"{result.Confidence:0}%";

        if (string.IsNullOrWhiteSpace(result.CardNumber))
        {
            CardNameText.Text = "-";
            MatchStatusText.Text =
                "Keine gültige Kartennummer erkannt.";

            ClearDatabaseDetails();
            return;
        }

        IReadOnlyList<LocalCardData> matches =
            ServiceLocator.Database.FindById(
                result.CardNumber);

        if (matches.Count == 0)
        {
            CardNameText.Text = "-";
            MatchStatusText.Text =
                "Kartennummer wurde erkannt, aber nicht in der lokalen Datenbank gefunden.";

            ClearDatabaseDetails(
                preserveRecognitionValues: true);

            return;
        }

        if (matches.Count == 1)
        {
            ShowCard(
                matches[0],
                "Eindeutiger Datenbanktreffer");

            return;
        }

        /*
         * Falls die erkannte Rarität genau eine Variante trifft,
         * darf diese Variante angezeigt werden.
         */
        List<LocalCardData> rarityMatches =
            matches
                .Where(card =>
                    !string.IsNullOrWhiteSpace(result.Rarity) &&
                    string.Equals(
                        card.Rarity,
                        result.Rarity,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (rarityMatches.Count == 1)
        {
            ShowCard(
                rarityMatches[0],
                $"Über Rarität aus {matches.Count} Varianten ausgewählt");

            return;
        }

        ShowAmbiguousCard(
            matches,
            result);
    }

    private void ShowCard(
        LocalCardData card,
        string matchStatus)
    {
        CardNameText.Text =
            DisplayValue(card.Name);

        CardNumberText.Text =
            DisplayValue(card.Id);

        RarityText.Text =
            DisplayValue(card.Rarity);

        CategoryText.Text =
            DisplayValue(card.Category);

        ColorsText.Text =
            FormatValues(card.Colors);

        CostText.Text =
            FormatNumber(card.Cost);

        PowerText.Text =
            FormatNumber(card.Power);

        CounterText.Text =
            FormatNumber(card.Counter);

        MatchStatusText.Text =
            matchStatus;

        LoadReferenceImage(
            card.LocalImagePath);
    }

    private void ShowAmbiguousCard(
        IReadOnlyList<LocalCardData> matches,
        RecognitionResult result)
    {
        LocalCardData firstCard =
            matches[0];

        CardNameText.Text =
            DisplayValue(firstCard.Name);

        CategoryText.Text =
            DisplayValue(firstCard.Category);

        ColorsText.Text =
            FormatValues(firstCard.Colors);

        CostText.Text =
            FormatNumber(firstCard.Cost);

        PowerText.Text =
            FormatNumber(firstCard.Power);

        CounterText.Text =
            FormatNumber(firstCard.Counter);

        MatchStatusText.Text =
            $"{matches.Count} Varianten mit der Nummer " +
            $"{result.CardNumber} gefunden. " +
            "Die genaue Alt-Art-/Parallel-Variante ist noch nicht eindeutig bestimmt.";

        /*
         * Bewusst kein beliebiges Referenzbild anzeigen.
         * Sonst könnte eine falsche Variante als Treffer erscheinen.
         */
        ReferenceCardImage.Source = null;
        ReferencePlaceholderText.Text =
            $"{matches.Count} Varianten gefunden";
        ReferencePlaceholderText.Visibility =
            Visibility.Visible;
    }

    private void LoadReferenceImage(
        string? imagePath)
    {
        ReferenceCardImage.Source = null;

        if (string.IsNullOrWhiteSpace(imagePath) ||
            !File.Exists(imagePath))
        {
            ReferencePlaceholderText.Text =
                "Kein lokales Referenzbild vorhanden";

            ReferencePlaceholderText.Visibility =
                Visibility.Visible;

            return;
        }

        try
        {
            ReferenceCardImage.Source =
                LoadBitmapFromFile(imagePath);

            ReferencePlaceholderText.Visibility =
                Visibility.Collapsed;
        }
        catch
        {
            ReferencePlaceholderText.Text =
                "Referenzbild konnte nicht geladen werden";

            ReferencePlaceholderText.Visibility =
                Visibility.Visible;
        }
    }

    private void SelectImage_Click(
    object sender,
    RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title =
                "One Piece Kartenbild auswählen",

            Filter =
                "Bilddateien|*.png;*.jpg;*.jpeg;*.bmp|" +
                "PNG-Dateien|*.png|" +
                "JPEG-Dateien|*.jpg;*.jpeg|" +
                "Alle Dateien|*.*"
        };

        bool? result =
            dialog.ShowDialog();

        if (result != true)
            return;

        try
        {
            _selectedImagePath =
                dialog.FileName;

            CardImage.Source =
                LoadBitmapFromFile(
                    _selectedImagePath);

            PlaceholderText.Visibility =
                Visibility.Collapsed;

            ResetCardDetails();

            Title =
                $"Void Century Vault – " +
                $"{Path.GetFileName(_selectedImagePath)}";

            if (AutoRecognizeCheckBox.IsChecked == true)
            {
                RecognizeSelectedImage();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Das Bild konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RemoveImage_Click(
        object sender,
        RoutedEventArgs e)
    {
        CardImage.Source = null;
        _selectedImagePath = null;

        PlaceholderText.Visibility =
            Visibility.Visible;

        ResetCardDetails();

        Title =
            "Void Century Vault";
    }

    private static BitmapImage LoadBitmapFromFile(
        string imagePath)
    {
        using FileStream stream = new(
            imagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        var bitmap =
            new BitmapImage();

        bitmap.BeginInit();
        bitmap.CacheOption =
            BitmapCacheOption.OnLoad;
        bitmap.StreamSource =
            stream;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    private void ResetCardDetails()
    {
        CardNameText.Text = "-";
        CardNumberText.Text = "-";
        LanguageText.Text = "-";
        RarityText.Text = "-";
        VariantText.Text = "-";
        CategoryText.Text = "-";
        ColorsText.Text = "-";
        CostText.Text = "-";
        PowerText.Text = "-";
        CounterText.Text = "-";
        ConfidenceText.Text = "-";
        MatchStatusText.Text = "-";

        ReferenceCardImage.Source = null;
        ReferencePlaceholderText.Text =
            "Noch keine Karte erkannt";
        ReferencePlaceholderText.Visibility =
            Visibility.Visible;
    }

    private void ClearDatabaseDetails(
        bool preserveRecognitionValues = false)
    {
        CardNameText.Text = "-";
        CategoryText.Text = "-";
        ColorsText.Text = "-";
        CostText.Text = "-";
        PowerText.Text = "-";
        CounterText.Text = "-";

        if (!preserveRecognitionValues)
        {
            RarityText.Text = "-";
            VariantText.Text = "-";
        }

        ReferenceCardImage.Source = null;
        ReferencePlaceholderText.Text =
            "Keine Kartendaten verfügbar";
        ReferencePlaceholderText.Visibility =
            Visibility.Visible;
    }

    private static string DisplayValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }

    private static string FormatValues(
        IEnumerable<string>? values)
    {
        if (values == null)
            return "-";

        string[] result =
            values
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .ToArray();

        return result.Length == 0
            ? "-"
            : string.Join(", ", result);
    }

    private static string FormatNumber(
        int? value)
    {
        return value?.ToString() ?? "-";
    }

    private void GenerateOcrTemplates_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBoxResult confirmation =
            MessageBox.Show(
                "Es werden aus den lokalen Referenzkarten neue " +
                "OCR-Zeichen-Templates erzeugt.\n\n" +
                "Der Vorgang kann einige Zeit dauern. Fortfahren?",
                "OCR-Templates erzeugen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor =
                System.Windows.Input.Cursors.Wait;

            var generator =
                new CharacterTemplateGenerator();

            CharacterTemplateGenerationResult result =
                generator.Generate(
                    maximumCards: 500);

            MessageBox.Show(
                $"Template-Erzeugung abgeschlossen.\n\n" +
                $"Verarbeitet: {result.ProcessedCards}\n" +
                $"Akzeptiert: {result.AcceptedCards}\n" +
                $"Abgelehnt: {result.RejectedCards}\n" +
                $"Übersprungen: {result.SkippedCards}\n" +
                $"Templates: {result.CreatedTemplates}\n\n" +
                $"{result.OutputFolder}",
                "OCR-Templates",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            OpenFolder(result.OutputFolder);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Fehler bei der Template-Erzeugung",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void GenerateOcrPreview_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Mouse.OverrideCursor =
                System.Windows.Input.Cursors.Wait;

            var generator =
                new OcrTemplatePreviewGenerator();

            OcrTemplatePreviewResult result =
                generator.Generate(
                    maxCards: int.MaxValue);

            MessageBox.Show(
                $"Template-Vorschau erstellt.\n\n" +
                $"Karten: {result.ProcessedCards}\n" +
                $"Rohbilder: {result.CreatedRawImages}\n" +
                $"Vorverarbeitete Bilder: " +
                $"{result.CreatedPreprocessedImages}\n" +
                $"Übersprungen: {result.SkippedCards}\n" +
                $"Fehler: {result.FailedCards}\n\n" +
                $"{result.OutputFolder}",
                "OCR-Vorschau",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            OpenFolder(result.OutputFolder);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Fehler bei der OCR-Vorschau",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void OpenTemplateViewer_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new TemplateViewerWindow
            {
                Owner = this
            };

        window.ShowDialog();
    }

    private static void OpenFolder(
        string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) ||
            !Directory.Exists(folderPath))
        {
            return;
        }

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
    }

    private void GenerateInventoryId_Click(
        object sender,
        RoutedEventArgs e)
    {
        var inventoryIdService =
            new InventoryIdService();

        string inventoryId =
            inventoryIdService.GenerateNextId();

        MessageBox.Show(
            $"Neue Inventar-ID:\n\n{inventoryId}",
            "Inventar-ID erzeugt",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SelectFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title =
                "Ordner mit Kartenbildern auswählen"
        };

        bool? dialogResult =
            dialog.ShowDialog();

        if (dialogResult != true)
            return;

        var batchImportService =
            new BatchImportService();

        List<string> images =
            batchImportService.LoadImages(
                dialog.FolderName);

        ImportList.Items.Clear();

        var recognitionService =
            new CardRecognitionService();

        var cardDatabase =
            new CardDatabaseService();

        foreach (string image in images)
        {
            try
            {
                RecognitionResult recognitionResult =
                    recognitionService.Recognize(
                        image);

                IReadOnlyList<CardData> variants =
                    cardDatabase.FindAllByCardNumber(
                        recognitionResult.CardNumber);

                if (variants.Count == 0)
                {
                    ImportList.Items.Add(
                        $"⚠ {recognitionResult.CardNumber} | " +
                        "Nicht in der alten Import-Datenbank");

                    continue;
                }

                CardData firstVariant =
                    variants[0];

                var inventoryManager =
                    new InventoryManager();

                CollectionItem inventoryItem =
                    inventoryManager.CreateInventoryItem(
                        firstVariant);

                ImportList.Items.Add(
                    $"✅ {inventoryItem.InventoryId} | " +
                    $"{recognitionResult.CardNumber} | " +
                    $"{firstVariant.Name} | " +
                    $"{variants.Count} Variante(n)");
            }
            catch (Exception exception)
            {
                ImportList.Items.Add(
                    $"❌ {Path.GetFileName(image)} | " +
                    $"{exception.Message}");
            }
        }

        MessageBox.Show(
            $"{images.Count} Bilder verarbeitet.",
            "Batch-Import",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LoadInventory()
    {
        var inventoryService =
            new InventoryService();

        InventoryGrid.ItemsSource =
            inventoryService.GetInventory();
    }

    private void ShowInventory_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadInventory();
    }

    private void OpenInventory_Click(
        object sender,
        RoutedEventArgs e)
    {
        InventoryWindow window = new();

        window.ShowDialog();
    }
}