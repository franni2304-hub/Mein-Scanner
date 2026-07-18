using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cv2 = OpenCvSharp.Cv2;
using ImreadModes = OpenCvSharp.ImreadModes;
using InterpolationFlags = OpenCvSharp.InterpolationFlags;
using Mat = OpenCvSharp.Mat;
using Size = OpenCvSharp.Size;
using ThresholdTypes = OpenCvSharp.ThresholdTypes;
using RetrievalModes = OpenCvSharp.RetrievalModes;
using ContourApproximationModes = OpenCvSharp.ContourApproximationModes;
using Rect = OpenCvSharp.Rect;
using Scalar = OpenCvSharp.Scalar;
using MatType = OpenCvSharp.MatType;

namespace OnePieceCardScanner.Views;

public partial class OcrTemplateAnalyzerWindow : System.Windows.Window
{
    private const int AnalysisWidth = 40;
    private const int AnalysisHeight = 56;

    private readonly Dictionary<char, List<TemplateRecord>> _templatesByCharacter = new();
    private readonly List<TemplateRecord> _allTemplates = new();
    private readonly string _templateFolder;

    public OcrTemplateAnalyzerWindow()
    {
        InitializeComponent();

        _templateFolder = IOPath.Combine(
            GetSolutionFolder(),
            "Data",
            "OCRTemplates");

        TemplateFolderText.Text = _templateFolder;

        Loaded += (_, _) => LoadTemplates();
        Closed += (_, _) => DisposeTemplates();
    }

    private void LoadTemplates()
    {
        DisposeTemplates();

        CharacterList.Items.Clear();
        TemplateList.ItemsSource = null;
        SimilarityList.ItemsSource = null;
        SelectedTemplateImage.Source = null;

        Directory.CreateDirectory(_templateFolder);

        foreach (string folder in Directory.GetDirectories(_templateFolder))
        {
            string folderName = IOPath.GetFileName(folder);

            if (!TryFolderToCharacter(folderName, out char character))
            {
                continue;
            }

            var records = new List<TemplateRecord>();

            foreach (string filePath in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
            {
                Mat source = Cv2.ImRead(filePath, ImreadModes.Grayscale);

                if (source.Empty())
                {
                    source.Dispose();
                    continue;
                }

                Mat normalized = NormalizeForAnalysis(source);
                source.Dispose();

                var record = new TemplateRecord
                {
                    Character = character,
                    FilePath = filePath,
                    FileName = IOPath.GetFileName(filePath),
                    Image = normalized,
                    Bitmap = ConvertMatToBitmap(normalized)
                };

                records.Add(record);
                _allTemplates.Add(record);
            }

            if (records.Count > 0)
            {
                _templatesByCharacter[character] = records;
            }
        }

        CalculateQualityScores();

        foreach (char character in _templatesByCharacter.Keys.OrderBy(GetCharacterOrder))
        {
            CharacterList.Items.Add(new CharacterListItem
            {
                Character = character,
                Count = _templatesByCharacter[character].Count
            });
        }

        GlobalStatisticsText.Text =
            $"Zeichenklassen: {_templatesByCharacter.Count} | Templates: {_allTemplates.Count}";

        StatusText.Text = "Analyse abgeschlossen.";

        if (CharacterList.Items.Count > 0)
        {
            CharacterList.SelectedIndex = 0;
        }
    }

    private void CalculateQualityScores()
    {
        foreach (List<TemplateRecord> classTemplates in _templatesByCharacter.Values)
        {
            foreach (TemplateRecord template in classTemplates)
            {
                List<double> sameClassScores = classTemplates
                    .Where(other => !ReferenceEquals(other, template))
                    .Select(other => CompareImages(template.Image, other.Image))
                    .OrderByDescending(score => score)
                    .Take(8)
                    .ToList();

                template.QualityScore = sameClassScores.Count == 0
                    ? 100
                    : sameClassScores.Average();
            }
        }
    }

    private void CharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharacterList.SelectedItem is CharacterListItem selected)
        {
            ShowCharacterClass(selected.Character);
        }
    }

    private void ShowCharacterClass(char character)
    {
        if (!_templatesByCharacter.TryGetValue(character, out List<TemplateRecord>? templates))
        {
            return;
        }

        TemplateList.ItemsSource = templates
            .OrderByDescending(template => template.QualityScore)
            .Select(template => new TemplateViewItem
            {
                Record = template,
                FileName = template.FileName,
                CharacterText = GetDisplayName(template.Character),
                Image = template.Bitmap,
                QualityText = $"{template.QualityScore:0.0} %",
                Background = template.QualityScore < 65
                    ? Brushes.MistyRose
                    : template.QualityScore < 80
                        ? Brushes.LemonChiffon
                        : Brushes.White,
                BorderBrush = template.QualityScore < 65
                    ? Brushes.Red
                    : template.QualityScore < 80
                        ? Brushes.DarkOrange
                        : Brushes.LightGray
            })
            .ToList();

        TemplateHeadingText.Text =
            $"Zeichen {GetDisplayName(character)} – {templates.Count} Templates";

        double averageQuality = templates.Average(template => template.QualityScore);
        double minimumQuality = templates.Min(template => template.QualityScore);
        double maximumQuality = templates.Max(template => template.QualityScore);

        ClassStatisticsText.Text =
            $"Anzahl: {templates.Count}\n" +
            $"Analysegröße: {AnalysisWidth} × {AnalysisHeight}\n" +
            $"Mittlere Klassenqualität: {averageQuality:0.0} %\n" +
            $"Schlechtester Wert: {minimumQuality:0.0} %\n" +
            $"Bester Wert: {maximumQuality:0.0} %";

        SelectedTemplateImage.Source = null;
        SelectedTemplateText.Text = "Kein Template ausgewählt";
        SimilarityList.ItemsSource = null;
    }

    private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedItem is TemplateViewItem selected)
        {
            ShowTemplateDetails(selected.Record);
        }
    }

    private void ShowTemplateDetails(TemplateRecord selected)
    {
        SelectedTemplateImage.Source = selected.Bitmap;

        SelectedTemplateText.Text =
            $"Zeichen: {GetDisplayName(selected.Character)}\n" +
            $"Qualität: {selected.QualityScore:0.0} %\n" +
            $"Datei: {selected.FileName}\n" +
            $"Pfad: {selected.FilePath}";

        SimilarityList.ItemsSource = _allTemplates
            .Where(template => !ReferenceEquals(template, selected))
            .Select(template => new SimilarityResult
            {
                Character = template.Character,
                FileName = template.FileName,
                Score = CompareImages(selected.Image, template.Image)
            })
            .OrderByDescending(result => result.Score)
            .Take(15)
            .ToList();
    }

    private void DeleteSelectedTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not TemplateViewItem selected)
        {
            MessageBox.Show("Bitte zuerst ein Template auswählen.");
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Template wirklich löschen?\n\n{selected.Record.FileName}",
            "Template löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(selected.Record.FilePath);
            LoadTemplates();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Template konnte nicht gelöscht werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        LoadTemplates();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _templateFolder,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message);
        }
    }

    private static Mat NormalizeForAnalysis(Mat source)
    {
        using Mat binary = new();

        Cv2.Threshold(
            source,
            binary,
            0,
            255,
            ThresholdTypes.Binary | ThresholdTypes.Otsu);

        int whitePixels =
            Cv2.CountNonZero(binary);

        int totalPixels =
            binary.Width * binary.Height;

        if (whitePixels > totalPixels / 2)
        {
            Cv2.BitwiseNot(
                binary,
                binary);
        }

        OpenCvSharp.Point[][] contours =
            Cv2.FindContoursAsArray(
                binary,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            return new Mat(
                AnalysisHeight,
                AnalysisWidth,
                MatType.CV_8UC1,
                Scalar.Black);
        }

        Rect bounds =
            Cv2.BoundingRect(
                contours.SelectMany(
                    contour => contour));

        const int cropPadding = 2;

        int x =
            Math.Max(
                0,
                bounds.X - cropPadding);

        int y =
            Math.Max(
                0,
                bounds.Y - cropPadding);

        int right =
            Math.Min(
                binary.Width,
                bounds.Right + cropPadding);

        int bottom =
            Math.Min(
                binary.Height,
                bounds.Bottom + cropPadding);

        Rect safeBounds =
            new Rect(
                x,
                y,
                Math.Max(1, right - x),
                Math.Max(1, bottom - y));

        using Mat cropped =
            new Mat(
                binary,
                safeBounds);

        const int canvasPadding = 3;

        double scale =
            Math.Min(
                (AnalysisWidth - canvasPadding * 2.0) /
                cropped.Width,
                (AnalysisHeight - canvasPadding * 2.0) /
                cropped.Height);

        int resizedWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    cropped.Width * scale));

        int resizedHeight =
            Math.Max(
                1,
                (int)Math.Round(
                    cropped.Height * scale));

        using Mat resized =
            new Mat();

        Cv2.Resize(
            cropped,
            resized,
            new Size(
                resizedWidth,
                resizedHeight),
            0,
            0,
            InterpolationFlags.Area);

        Mat normalized =
            new Mat(
                AnalysisHeight,
                AnalysisWidth,
                MatType.CV_8UC1,
                Scalar.Black);

        int destinationX =
            (AnalysisWidth - resizedWidth) / 2;

        int destinationY =
            (AnalysisHeight - resizedHeight) / 2;

        using Mat destination =
            new Mat(
                normalized,
                new Rect(
                    destinationX,
                    destinationY,
                    resizedWidth,
                    resizedHeight));

        resized.CopyTo(
            destination);

        return normalized;
    }

    private static double CompareImages(Mat left, Mat right)
    {
        using Mat difference = new();
        Cv2.Absdiff(left, right, difference);

        double meanDifference = Cv2.Mean(difference).Val0;

        return Math.Clamp(
            100.0 - meanDifference / 255.0 * 100.0,
            0,
            100);
    }

    private static BitmapImage ConvertMatToBitmap(Mat image)
    {
        Cv2.ImEncode(".png", image, out byte[] bytes);

        using MemoryStream stream = new(bytes);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    private static bool TryFolderToCharacter(string folderName, out char character)
    {
        if (string.Equals(folderName, "Dash", StringComparison.OrdinalIgnoreCase))
        {
            character = '-';
            return true;
        }

        if (string.Equals(folderName, "Slash", StringComparison.OrdinalIgnoreCase))
        {
            character = '/';
            return true;
        }

        if (string.Equals(folderName, "Backslash", StringComparison.OrdinalIgnoreCase))
        {
            character = '\\';
            return true;
        }

        if (folderName.Length == 1)
        {
            character = char.ToUpperInvariant(folderName[0]);
            return true;
        }

        character = default;
        return false;
    }

    private static int GetCharacterOrder(char character)
    {
        const string order = "0123456789OPEBSTR-/\\";
        int index = order.IndexOf(character);
        return index < 0 ? 100 : index;
    }

    private static string GetDisplayName(char character)
    {
        return character switch
        {
            '-' => "Bindestrich",
            '/' => "Slash",
            '\\' => "Backslash",
            _ => character.ToString()
        };
    }

    private static string GetSolutionFolder()
    {
        return IOPath.GetFullPath(
            IOPath.Combine(
                AppContext.BaseDirectory,
                @"..\..\..\.."));
    }

    private void DisposeTemplates()
    {
        foreach (TemplateRecord template in _allTemplates)
        {
            template.Image.Dispose();
        }

        _allTemplates.Clear();
        _templatesByCharacter.Clear();
    }

    private sealed class CharacterListItem
    {
        public char Character { get; init; }
        public int Count { get; init; }

        public override string ToString()
        {
            return $"{GetDisplayName(Character)} ({Count})";
        }
    }

    private sealed class TemplateRecord
    {
        public char Character { get; init; }
        public string FilePath { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public Mat Image { get; init; } = new();
        public BitmapImage? Bitmap { get; init; }
        public double QualityScore { get; set; }
    }

    private sealed class TemplateViewItem
    {
        public TemplateRecord Record { get; init; } = null!;
        public string FileName { get; init; } = string.Empty;
        public string CharacterText { get; init; } = string.Empty;
        public string QualityText { get; init; } = string.Empty;
        public BitmapImage? Image { get; init; }
        public Brush Background { get; init; } = Brushes.White;
        public Brush BorderBrush { get; init; } = Brushes.LightGray;
    }

    private sealed class SimilarityResult
    {
        public char Character { get; init; }
        public string FileName { get; init; } = string.Empty;
        public double Score { get; init; }

        public override string ToString()
        {
            return $"{GetDisplayName(Character),-12} {Score,6:0.0} %  {FileName}";
        }
    }
}
