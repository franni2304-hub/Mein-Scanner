using Microsoft.Win32;
using OnePieceCardScanner.Recognition;
using OnePieceCardScanner.Recognition.Segmentation;
using OnePieceCardScanner.Recognition.TemplateMatching;
using OnePieceCardScanner.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cv2 = OpenCvSharp.Cv2;
using Mat = OpenCvSharp.Mat;
using Rect = OpenCvSharp.Rect;

namespace OnePieceCardScanner.Views;

public partial class SegmentTrainingWindow : System.Windows.Window
{
    private readonly CharacterSegmenter _segmenter =
        new();

    private CharacterTemplateLibrary? _templateLibrary;

    private CharacterMatcher? _characterMatcher;

    private readonly List<CharacterSegment> _segments =
        [];

    private readonly List<int> _selectedSegmentIndexes =
        [];

    private readonly Random _random =
        new();

    private List<string> _previewFiles =
        [];

    private HashSet<string> _trainedFiles =
        new(
            StringComparer.OrdinalIgnoreCase);

    private string? _sourceImagePath;
    private string _expectedText = string.Empty;
    private int _currentCharacterIndex;
    private int? _splitSelectedIndex;

    public SegmentTrainingWindow()
    {
        InitializeComponent();

        Closed += (_, _) =>
        {
            DisposeSegments();
            DisposeMatcher();
        };

        LoadPreviewFileList();
        LoadMatcher();
    }

    private void LoadPreviewFileList()
    {
        string folder =
            GetPreviewFolder();

        PreviewFolderText.Text =
            folder;

        if (!Directory.Exists(folder))
        {
            PreviewCounterText.Text =
                "Ordner nicht gefunden. Erzeuge zuerst die OCR-Vorschau.";

            _previewFiles =
                [];

            return;
        }

        _previewFiles =
            Directory.GetFiles(
                folder,
                "*.png",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path)
            .ToList();

        _trainedFiles =
            LoadTrainingHistory();

        UpdatePreviewCounter();
    }

    private void LoadRandomPreview_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadRandomPreview();
    }

    private void LoadRandomPreview()
    {
        if (_previewFiles.Count == 0)
        {
            MessageBox.Show(
                "Im Preview-Ordner wurden keine PNG-Dateien gefunden.");

            return;
        }

        List<string> remaining =
            _previewFiles
                .Where(path =>
                    !_trainedFiles.Contains(
                        Path.GetFullPath(path)))
                .ToList();

        if (remaining.Count == 0)
        {
            MessageBox.Show(
                "Alle vorhandenen Preview-Bilder wurden bereits trainiert.");

            return;
        }

        string selected =
            remaining[
                _random.Next(
                    remaining.Count)];

        LoadSourceImage(
            selected);

        ExpectedTextBox.Text =
            ExtractPrintedNumberFromPreviewFile(
                selected);

        StartTraining();
    }

    private void DeleteCurrentPreview_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _sourceImagePath) ||
            !File.Exists(
                _sourceImagePath))
        {
            MessageBox.Show(
                "Es ist kein löschbares Preview-Bild geladen.");

            return;
        }

        string fullPath =
            Path.GetFullPath(
                _sourceImagePath);

        string previewFolder =
            Path.GetFullPath(
                GetPreviewFolder());

        if (!fullPath.StartsWith(
                previewFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Eigene Bilder werden aus Sicherheitsgründen nicht gelöscht.");

            return;
        }

        MessageBoxResult result =
            MessageBox.Show(
                $"Dieses unbrauchbare Preview-Bild wirklich löschen?\n\n" +
                $"{Path.GetFileName(fullPath)}\n\n" +
                "Der passende Raw-Ausschnitt wird ebenfalls gelöscht, falls vorhanden.",
                "Preview löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            DisposeSegments();

            File.Delete(
                fullPath);

            string rawPath =
                Path.Combine(
                    GetRawPreviewFolder(),
                    Path.GetFileName(fullPath));

            if (File.Exists(rawPath))
            {
                File.Delete(
                    rawPath);
            }

            _previewFiles.RemoveAll(path =>
                string.Equals(
                    Path.GetFullPath(path),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));

            _trainedFiles.Remove(
                fullPath);

            SourceImage.Source =
                null;

            _sourceImagePath =
                null;

            ExpectedTextBox.Clear();

            StatusText.Text =
                "Preview gelöscht.";

            UpdatePreviewCounter();
            LoadRandomPreview();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Preview konnte nicht gelöscht werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenImage_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new OpenFileDialog
            {
                Title =
                    "Vorverarbeitetes Nummernbild auswählen",

                Filter =
                    "Bilddateien|*.png;*.jpg;*.jpeg;*.bmp|" +
                    "PNG-Dateien|*.png|" +
                    "Alle Dateien|*.*"
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        LoadSourceImage(
            dialog.FileName);

        ExpectedTextBox.Text =
            ExtractPrintedNumberFromPreviewFile(
                dialog.FileName);
    }

    private void LoadSourceImage(
        string imagePath)
    {
        _sourceImagePath =
            imagePath;

        SourceImage.Source =
            LoadBitmap(
                imagePath);

        StatusText.Text =
            Path.GetFileName(
                imagePath);

        InstructionText.Text =
            "Kartennummer prüfen und Segmentierung starten.";

        ResetTrainingState();
    }

    private void StartTraining_Click(
        object sender,
        RoutedEventArgs e)
    {
        StartTraining();
    }

    private void StartTraining()
    {
        if (string.IsNullOrWhiteSpace(
                _sourceImagePath) ||
            !File.Exists(
                _sourceImagePath))
        {
            MessageBox.Show(
                "Bitte zuerst ein Bild öffnen.");

            return;
        }

        string expectedText =
            ExpectedTextBox.Text
                .Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(
                expectedText))
        {
            MessageBox.Show(
                "Bitte die echte Kartennummer eingeben.");

            return;
        }

        DisposeSegments();

        _expectedText =
            expectedText;

        _currentCharacterIndex =
            0;

        _selectedSegmentIndexes.Clear();

        _splitSelectedIndex =
            null;

        IReadOnlyList<CharacterSegment> detected =
            _segmenter.Segment(
                _sourceImagePath);

        _segments.AddRange(
            detected);

        ReindexSegments();
        RefreshSegmentList();
        UpdateInstruction();
        ClearSplitSelection();
    }

    private void Segment_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not int segmentIndex)
        {
            return;
        }

        if (SplitModeToggle.IsChecked == true)
        {
            SelectSegmentForSplit(
                segmentIndex);

            SplitModeToggle.IsChecked =
                false;

            return;
        }

        ShowMatcherResults(
            segmentIndex);

        if (_currentCharacterIndex >=
            _expectedText.Length)
        {
            return;
        }

        if (_selectedSegmentIndexes.Contains(
                segmentIndex))
        {
            StatusText.Text =
                "Dieses Segment wurde bereits verwendet.";

            RefreshSegmentList(
                duplicateSegmentIndex:
                    segmentIndex);

            return;
        }

        _selectedSegmentIndexes.Add(
            segmentIndex);

        _currentCharacterIndex++;

        RefreshSegmentList();
        UpdateInstruction();
    }

    private void SelectSegmentForSplit(
        int segmentIndex)
    {
        if (segmentIndex < 0 ||
            segmentIndex >= _segments.Count)
        {
            return;
        }

        if (_selectedSegmentIndexes.Contains(
                segmentIndex))
        {
            MessageBox.Show(
                "Ein bereits zugeordnetes Segment kann nicht geteilt werden. " +
                "Mache die Zuordnung zuerst rückgängig.");

            return;
        }

        _splitSelectedIndex =
            segmentIndex;

        CharacterSegment segment =
            _segments[segmentIndex];

        SplitPreviewImage.Source =
            ConvertMatToBitmap(
                segment.Image);

        SplitSlider.Minimum =
            1;

        SplitSlider.Maximum =
            Math.Max(
                2,
                segment.Image.Width - 1);

        SplitSlider.Value =
            Math.Max(
                1,
                segment.Image.Width / 2.0);

        SplitSlider.IsEnabled =
            segment.Image.Width >= 4;

        SplitSegmentButton.IsEnabled =
            segment.Image.Width >= 4;

        SplitSelectionText.Text =
            $"Segment {segmentIndex}: " +
            $"{segment.Image.Width} × " +
            $"{segment.Image.Height} Pixel";

        UpdateSplitPositionText();
        UpdateSplitPreview();
        RefreshSegmentList();
    }

    private void SplitSelectedSegment_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_splitSelectedIndex is not int segmentIndex ||
            segmentIndex < 0 ||
            segmentIndex >= _segments.Count)
        {
            return;
        }

        CharacterSegment original =
            _segments[segmentIndex];

        int splitX =
            (int)Math.Round(
                SplitSlider.Value);

        if (splitX < 1 ||
            splitX >= original.Image.Width)
        {
            MessageBox.Show(
                "Die Trennposition ist ungültig.");

            return;
        }

        using Mat leftView =
            new Mat(
                original.Image,
                new Rect(
                    0,
                    0,
                    splitX,
                    original.Image.Height));

        using Mat rightView =
            new Mat(
                original.Image,
                new Rect(
                    splitX,
                    0,
                    original.Image.Width - splitX,
                    original.Image.Height));

        Mat leftImage =
            leftView.Clone();

        Mat rightImage =
            rightView.Clone();

        int leftBoundsWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    original.Bounds.Width *
                    (splitX /
                     (double)original.Image.Width)));

        int rightBoundsWidth =
            Math.Max(
                1,
                original.Bounds.Width -
                leftBoundsWidth);

        var leftSegment =
            new CharacterSegment
            {
                Bounds =
                    new Rect(
                        original.Bounds.X,
                        original.Bounds.Y,
                        leftBoundsWidth,
                        original.Bounds.Height),

                Image =
                    leftImage
            };

        var rightSegment =
            new CharacterSegment
            {
                Bounds =
                    new Rect(
                        original.Bounds.X +
                        leftBoundsWidth,
                        original.Bounds.Y,
                        rightBoundsWidth,
                        original.Bounds.Height),

                Image =
                    rightImage
            };

        original.Image.Dispose();

        _segments.RemoveAt(
            segmentIndex);

        _segments.Insert(
            segmentIndex,
            rightSegment);

        _segments.Insert(
            segmentIndex,
            leftSegment);

        ShiftAssignedIndexesAfterSplit(
            segmentIndex);

        ReindexSegments();
        ClearSplitSelection();
        RefreshSegmentList();
        UpdateInstruction();

        StatusText.Text =
            $"Segment {segmentIndex} wurde geteilt.";
    }

    private void ShiftAssignedIndexesAfterSplit(
        int splitIndex)
    {
        for (int index = 0;
             index < _selectedSegmentIndexes.Count;
             index++)
        {
            if (_selectedSegmentIndexes[index] >
                splitIndex)
            {
                _selectedSegmentIndexes[index]++;
            }
        }
    }

    private void SplitModeChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (SplitModeToggle.IsChecked == true)
        {
            InstructionText.Text =
                "Teilmodus: Klicke das Segment an, das zwei Zeichen enthält.";

            StatusText.Text =
                "Teilmodus aktiviert.";

            return;
        }

        if (_splitSelectedIndex != null)
        {
            InstructionText.Text =
                "Stelle rechts die Trennposition ein und teile das Segment.";

            StatusText.Text =
                "Segment zum Teilen ausgewählt.";

            return;
        }

        UpdateInstruction();
    }

    private void SplitSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        UpdateSplitPositionText();
        UpdateSplitPreview();
    }

    private void MoveSplitLeft_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SplitSlider == null ||
            !SplitSlider.IsEnabled)
        {
            return;
        }

        SplitSlider.Value =
            Math.Max(
                SplitSlider.Minimum,
                SplitSlider.Value - 1);
    }

    private void MoveSplitRight_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SplitSlider == null ||
            !SplitSlider.IsEnabled)
        {
            return;
        }

        SplitSlider.Value =
            Math.Min(
                SplitSlider.Maximum,
                SplitSlider.Value + 1);
    }

    private void UpdateSplitPositionText()
    {
        if (SplitPositionText == null ||
            SplitSlider == null)
        {
            return;
        }

        if (_splitSelectedIndex == null)
        {
            SplitPositionText.Text =
                "Trennposition: -";

            return;
        }

        SplitPositionText.Text =
            $"Trennposition: " +
            $"{SplitSlider.Value:0.00} Pixel";
    }

    private void UpdateSplitPreview()
    {
        if (SplitPreviewImage == null ||
            SplitSlider == null ||
            _splitSelectedIndex is not int segmentIndex ||
            segmentIndex < 0 ||
            segmentIndex >= _segments.Count)
        {
            return;
        }

        using Mat preview =
            _segments[segmentIndex].Image.Clone();

        int x =
            Math.Clamp(
                (int)Math.Round(SplitSlider.Value),
                0,
                preview.Width - 1);

        Cv2.Line(
            preview,
            new OpenCvSharp.Point(x, 0),
            new OpenCvSharp.Point(x, preview.Height - 1),
            new OpenCvSharp.Scalar(255),
            1);

        SplitPreviewImage.Source =
            ConvertMatToBitmap(
                preview);
    }

    private void Undo_Click(
        object sender,
        RoutedEventArgs e)
    {
        UndoLastAssignment();
    }

    private void Reset_Click(
        object sender,
        RoutedEventArgs e)
    {
        _selectedSegmentIndexes.Clear();

        _currentCharacterIndex =
            0;

        ClearSplitSelection();
        RefreshSegmentList();
        UpdateInstruction();

        StatusText.Text =
            "Zuordnung zurückgesetzt.";
    }

    private void SaveAssignments_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveAssignments(
            loadNext: false);
    }

    private void SaveAndNext_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveAssignments(
            loadNext: true);
    }

    private void SaveAssignments(
        bool loadNext)
    {
        if (_segments.Count == 0)
        {
            MessageBox.Show(
                "Es wurden noch keine Segmente erzeugt.");

            return;
        }

        if (_selectedSegmentIndexes.Count !=
            _expectedText.Length)
        {
            MessageBox.Show(
                $"Es wurden {_selectedSegmentIndexes.Count} von " +
                $"{_expectedText.Length} Zeichen zugeordnet.");

            return;
        }

        string templateRoot =
            GetTemplateRootFolder();

        string sourceName =
            Path.GetFileNameWithoutExtension(
                _sourceImagePath) ??
            "source";

        string batchId =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmssfff");

        for (int index = 0;
             index < _expectedText.Length;
             index++)
        {
            char character =
                _expectedText[index];

            int segmentIndex =
                _selectedSegmentIndexes[index];

            CharacterSegment segment =
                _segments[segmentIndex];

            string folderName =
                GetCharacterFolderName(
                    character);

            string characterFolder =
                Path.Combine(
                    templateRoot,
                    folderName);

            Directory.CreateDirectory(
                characterFolder);

            string outputPath =
                Path.Combine(
                    characterFolder,
                    $"{MakeSafeFileName(sourceName)}_" +
                    $"{batchId}_" +
                    $"p{index:00}_s{segmentIndex:000}.png");

            Cv2.ImWrite(
                outputPath,
                segment.Image);
        }

        RegisterCurrentFileAsTrained();

        StatusText.Text =
            "Zuordnung gespeichert.";

        if (loadNext)
        {
            LoadRandomPreview();
        }
        else
        {
            MessageBox.Show(
                $"{_expectedText.Length} bestätigte Templates wurden gespeichert.",
                "Training gespeichert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void RegisterCurrentFileAsTrained()
    {
        if (string.IsNullOrWhiteSpace(
                _sourceImagePath))
        {
            return;
        }

        string fullPath =
            Path.GetFullPath(
                _sourceImagePath);

        _trainedFiles.Add(
            fullPath);

        File.AppendAllLines(
            GetTrainingHistoryPath(),
            [fullPath]);

        UpdatePreviewCounter();
    }

    private void RefreshSegmentList(
        int? duplicateSegmentIndex = null)
    {
        var items =
            new List<SegmentViewItem>();

        for (int index = 0;
             index < _segments.Count;
             index++)
        {
            int assignmentPosition =
                _selectedSegmentIndexes.IndexOf(
                    index);

            bool assigned =
                assignmentPosition >= 0;

            bool duplicate =
                duplicateSegmentIndex == index;

            bool splitSelected =
                _splitSelectedIndex == index;

            string assignment =
                assigned &&
                assignmentPosition <
                _expectedText.Length
                    ? $"✓ {_expectedText[assignmentPosition]}"
                    : splitSelected
                        ? "TEILEN"
                        : string.Empty;

            items.Add(
                new SegmentViewItem
                {
                    Index =
                        index,

                    IndexLabel =
                        $"Segment {index}",

                    AssignmentLabel =
                        assignment,

                    Image =
                        ConvertMatToBitmap(
                            _segments[index].Image),

                    Background =
                        duplicate
                            ? Brushes.MistyRose
                            : splitSelected
                                ? Brushes.LemonChiffon
                                : assigned
                                    ? Brushes.Honeydew
                                    : Brushes.White,

                    BorderBrush =
                        duplicate
                            ? Brushes.Red
                            : splitSelected
                                ? Brushes.DarkOrange
                                : assigned
                                    ? Brushes.Green
                                    : Brushes.LightGray
                });
        }

        SegmentItems.ItemsSource =
            items;
    }

    private void UpdateInstruction()
    {
        if (SplitModeToggle.IsChecked == true)
        {
            InstructionText.Text =
                "Teilmodus: Wähle ein Segment aus und teile es rechts.";

            return;
        }

        if (_segments.Count == 0)
        {
            InstructionText.Text =
                "Keine Segmente vorhanden.";

            ProgressText.Text =
                string.Empty;

            return;
        }

        if (_currentCharacterIndex >=
            _expectedText.Length)
        {
            InstructionText.Text =
                "Alle Zeichen wurden zugeordnet. Prüfe die grünen Felder und speichere.";

            ProgressText.Text =
                $"{_selectedSegmentIndexes.Count} von " +
                $"{_expectedText.Length} Zeichen zugeordnet.";

            return;
        }

        char expectedCharacter =
            _expectedText[
                _currentCharacterIndex];

        InstructionText.Text =
            $"Markiere jetzt das Segment für: " +
            $"{GetDisplayName(expectedCharacter)}";

        ProgressText.Text =
            $"Position {_currentCharacterIndex + 1} von " +
            $"{_expectedText.Length} – {_expectedText}";
    }

    private void UndoLastAssignment()
    {
        if (_selectedSegmentIndexes.Count == 0)
        {
            return;
        }

        _selectedSegmentIndexes.RemoveAt(
            _selectedSegmentIndexes.Count - 1);

        _currentCharacterIndex =
            Math.Max(
                0,
                _currentCharacterIndex - 1);

        RefreshSegmentList();
        UpdateInstruction();
    }

    private void ResetTrainingState()
    {
        DisposeSegments();

        _expectedText =
            string.Empty;

        _currentCharacterIndex =
            0;

        _selectedSegmentIndexes.Clear();

        ClearSplitSelection();
    }

    private void ClearSplitSelection()
    {
        _splitSelectedIndex =
            null;

        SplitPreviewImage.Source =
            null;

        SplitSelectionText.Text =
            "Kein Segment ausgewählt";

        SplitPositionText.Text =
            "Trennposition: -";

        SplitSlider.IsEnabled =
            false;

        SplitSegmentButton.IsEnabled =
            false;
    }

    private void ReindexSegments()
    {
        for (int index = 0;
             index < _segments.Count;
             index++)
        {
            _segments[index].Position =
                index;
        }
    }

    private void Window_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            SplitModeToggle.IsChecked =
                SplitModeToggle.IsChecked != true;

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z &&
            Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control))
        {
            UndoLastAssignment();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Reset_Click(
                this,
                new RoutedEventArgs());

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            _currentCharacterIndex >=
            _expectedText.Length &&
            _segments.Count > 0)
        {
            SaveAssignments(
                loadNext: true);

            e.Handled = true;
        }
    }

    private void LoadMatcher()
    {
        try
        {
            DisposeMatcher();

            string templateFolder =
                GetTemplateRootFolder();

            var library =
                new CharacterTemplateLibrary();

            library.Load(
                templateFolder);

            if (!library.GetAllTemplates().Any())
            {
                MatcherStatusText.Text =
                    "Noch keine Templates gefunden.";

                return;
            }

            _templateLibrary =
                library;

            _characterMatcher =
                new CharacterMatcher(
                    library);

            MatcherStatusText.Text =
                $"Matcher bereit: " +
                $"{library.GetAllTemplates().Count()} Templates geladen.";
        }
        catch (Exception exception)
        {
            MatcherStatusText.Text =
                "Matcher konnte nicht geladen werden: " +
                exception.Message;
        }
    }

    private void ShowMatcherResults(
        int segmentIndex)
    {
        if (segmentIndex < 0 ||
            segmentIndex >= _segments.Count)
        {
            return;
        }

        CharacterSegment segment =
            _segments[segmentIndex];

        SelectedSegmentImage.Source =
            ConvertMatToBitmap(
                segment.Image);

        if (_characterMatcher == null)
        {
            LoadMatcher();
        }

        if (_characterMatcher == null)
        {
            MatcherResultsList.ItemsSource =
                null;

            BestTemplateImage.Source =
                null;

            BestTemplateText.Text =
                "Matcher ist nicht verfügbar.";

            return;
        }

        IReadOnlyList<CharacterMatch> matches =
            _characterMatcher.Match(
                segment.Image,
                top: 10);

        List<MatcherResultViewItem> viewItems =
            matches
                .Select((match, index) =>
                    new MatcherResultViewItem
                    {
                        Rank =
                            $"{index + 1}.",

                        CharacterText =
                            GetDisplayName(
                                match.Character),

                        ScoreText =
                            $"{match.Score:0.0} %",

                        DetailText =
                            $"Bester Einzelwert: " +
                            $"{match.BestTemplateScore:0.0} % | " +
                            $"{match.ComparedTemplateCount} Vorlagen",

                        Match =
                            match
                    })
                .ToList();

        MatcherResultsList.ItemsSource =
            viewItems;

        MatcherStatusText.Text =
            matches.Count == 0
                ? "Für dieses Segment wurden keine Treffer gefunden."
                : $"Segment {segmentIndex}: " +
                  $"Vorschlag {GetDisplayName(matches[0].Character)} " +
                  $"mit {matches[0].Score:0.0} %";

        if (viewItems.Count > 0)
        {
            MatcherResultsList.SelectedIndex =
                0;
        }
        else
        {
            BestTemplateImage.Source =
                null;

            BestTemplateText.Text =
                "Noch kein Treffer";
        }
    }

    private void MatcherResultsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (MatcherResultsList.SelectedItem
            is not MatcherResultViewItem selected)
        {
            return;
        }

        CharacterTemplate template =
            selected.Match.BestTemplate;

        BestTemplateImage.Source =
            ConvertMatToBitmap(
                template.Image);

        BestTemplateText.Text =
            $"Zeichen: " +
            $"{GetDisplayName(selected.Match.Character)}\n" +
            $"Klassenwert: {selected.Match.Score:0.0} %\n" +
            $"Bestes Template: " +
            $"{selected.Match.BestTemplateScore:0.0} %\n" +
            $"Datei: {Path.GetFileName(template.FilePath)}";
    }

    private void DisposeMatcher()
    {
        _characterMatcher?.Dispose();

        _characterMatcher =
            null;

        if (_templateLibrary != null)
        {
            foreach (CharacterTemplate template in
                     _templateLibrary.GetAllTemplates())
            {
                template.Image.Dispose();
            }
        }

        _templateLibrary =
            null;
    }

    private void UpdatePreviewCounter()
    {
        int trainedImageCount =
            _previewFiles.Count(path =>
                _trainedFiles.Contains(
                    Path.GetFullPath(path)));

        int trainedCardCount =
            _trainedFiles
                .Select(GetCardIdFromPreviewPath)
                .Where(cardId =>
                    !string.IsNullOrWhiteSpace(cardId))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        int storedSegmentCount =
            CountStoredTemplates();

        PreviewCounterText.Text =
            $"Bilder: {_previewFiles.Count} | " +
            $"trainiert: {trainedImageCount} | " +
            $"offen: {_previewFiles.Count - trainedImageCount}";

        ManualTrainingCountText.Text =
            $"Karten angelernt: {trainedCardCount}";

        ManualSegmentCountText.Text =
            $"Segmente gespeichert: {storedSegmentCount}";
    }

    private static string GetCardIdFromPreviewPath(
        string imagePath)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(
                imagePath);

        return Regex.Replace(
            fileName,
            @"_c\d+$",
            string.Empty,
            RegexOptions.IgnoreCase);
    }

    private static int CountStoredTemplates()
    {
        string templateRoot =
            GetTemplateRootFolder();

        int count = 0;

        foreach (string folder in
                 Directory.GetDirectories(
                     templateRoot))
        {
            string folderName =
                Path.GetFileName(folder);

            if (string.Equals(
                    folderName,
                    "Rejected",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count +=
                Directory.GetFiles(
                    folder,
                    "*.png",
                    SearchOption.TopDirectoryOnly)
                .Length;
        }

        return count;
    }

    private static string ExtractPrintedNumberFromPreviewFile(
        string imagePath)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(
                imagePath);

        string cardId =
            Regex.Replace(
                fileName,
                @"_c\d+$",
                string.Empty,
                RegexOptions.IgnoreCase);

        return LocalCardDatabaseService
            .GetPrintedCardNumber(
                cardId);
    }

    private static HashSet<string> LoadTrainingHistory()
    {
        string path =
            GetTrainingHistoryPath();

        if (!File.Exists(path))
        {
            return new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        }

        return File.ReadAllLines(path)
            .Where(line =>
                !string.IsNullOrWhiteSpace(line))
            .Select(Path.GetFullPath)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);
    }

    private static string GetPreviewFolder()
    {
        return Path.Combine(
            GetSolutionFolder(),
            "Data",
            "OCRTemplatePreview",
            "Preprocessed");
    }

    private static string GetRawPreviewFolder()
    {
        return Path.Combine(
            GetSolutionFolder(),
            "Data",
            "OCRTemplatePreview",
            "Raw");
    }

    private static string GetTemplateRootFolder()
    {
        string folder =
            Path.Combine(
                GetSolutionFolder(),
                "Data",
                "OCRTemplates");

        Directory.CreateDirectory(
            folder);

        return folder;
    }

    private static string GetTrainingHistoryPath()
    {
        string folder =
            GetTemplateRootFolder();

        return Path.Combine(
            folder,
            "training-history.txt");
    }

    private static string GetSolutionFolder()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                @"..\..\..\.."));
    }

    private static BitmapImage ConvertMatToBitmap(
        Mat image)
    {
        Cv2.ImEncode(
            ".png",
            image,
            out byte[] bytes);

        using var stream =
            new MemoryStream(
                bytes);

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

    private static BitmapImage LoadBitmap(
        string imagePath)
    {
        using FileStream stream =
            new(
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

    private static string GetCharacterFolderName(
        char character)
    {
        return character switch
        {
            '-' => "Dash",
            '/' => "Slash",
            '\\' => "Backslash",
            _ => character.ToString()
        };
    }

    private static string GetDisplayName(
        char character)
    {
        return character switch
        {
            '-' => "Bindestrich (-)",
            _ => character.ToString()
        };
    }

    private static string MakeSafeFileName(
        string value)
    {
        char[] invalid =
            Path.GetInvalidFileNameChars();

        return new string(
            value
                .Select(character =>
                    invalid.Contains(character)
                        ? '_'
                        : character)
                .ToArray());
    }

    private void DisposeSegments()
    {
        foreach (CharacterSegment segment in
                 _segments)
        {
            segment.Image.Dispose();
        }

        _segments.Clear();
        SegmentItems.ItemsSource =
            null;
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private sealed class MatcherResultViewItem
    {
        public string Rank { get; init; } =
            string.Empty;

        public string CharacterText { get; init; } =
            string.Empty;

        public string ScoreText { get; init; } =
            string.Empty;

        public string DetailText { get; init; } =
            string.Empty;

        public CharacterMatch Match { get; init; } =
            null!;
    }

    private sealed class SegmentViewItem
    {
        public int Index { get; init; }

        public string IndexLabel { get; init; } =
            string.Empty;

        public string AssignmentLabel { get; init; } =
            string.Empty;

        public BitmapImage? Image { get; init; }

        public Brush Background { get; init; } =
            Brushes.White;

        public Brush BorderBrush { get; init; } =
            Brushes.LightGray;
    }
}