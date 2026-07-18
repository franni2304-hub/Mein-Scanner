using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OnePieceCardScanner.Views;

public partial class TemplateTrainerWindow : Window
{
    private BitmapSource? _loadedBitmap;

    private Point _selectionStart;

    private bool _isSelecting;

    private Int32Rect _selectedPixelArea;

    public TemplateTrainerWindow()
    {
        InitializeComponent();

        ImageCanvas.MouseLeftButtonDown +=
            ImageCanvas_MouseLeftButtonDown;

        ImageCanvas.MouseMove +=
            ImageCanvas_MouseMove;

        ImageCanvas.MouseLeftButtonUp +=
            ImageCanvas_MouseLeftButtonUp;

        StatusText.Text =
            "Öffne ein Nummernbild und ziehe ein Rechteck um ein Zeichen.";
    }

    private void OpenImageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new OpenFileDialog
            {
                Title =
                    "Nummernbild auswählen",

                Filter =
                    "Bilddateien|" +
                    "*.png;*.jpg;*.jpeg;*.bmp|" +
                    "PNG-Dateien|*.png|" +
                    "Alle Dateien|*.*"
            };

        bool? result =
            dialog.ShowDialog();

        if (result != true)
        {
            return;
        }

        try
        {
            _loadedBitmap =
                LoadBitmap(
                    dialog.FileName);

            MainImage.Source =
                _loadedBitmap;

            MainImage.Width =
                _loadedBitmap.PixelWidth;

            MainImage.Height =
                _loadedBitmap.PixelHeight;

            ImageCanvas.Width =
                _loadedBitmap.PixelWidth;

            ImageCanvas.Height =
                _loadedBitmap.PixelHeight;

            Canvas.SetLeft(
                MainImage,
                0);

            Canvas.SetTop(
                MainImage,
                0);

            SelectionRectangle.Visibility =
                Visibility.Collapsed;

            _selectedPixelArea =
                new Int32Rect();

            StatusText.Text =
                $"Bild geladen: " +
                $"{Path.GetFileName(dialog.FileName)} " +
                $"({_loadedBitmap.PixelWidth} × " +
                $"{_loadedBitmap.PixelHeight})";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Bild konnte nicht geladen werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImageCanvas_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_loadedBitmap == null)
        {
            return;
        }

        _selectionStart =
            ClampPointToImage(
                e.GetPosition(
                    ImageCanvas));

        _isSelecting =
            true;

        ImageCanvas.CaptureMouse();

        Canvas.SetLeft(
            SelectionRectangle,
            _selectionStart.X);

        Canvas.SetTop(
            SelectionRectangle,
            _selectionStart.Y);

        SelectionRectangle.Width =
            0;

        SelectionRectangle.Height =
            0;

        SelectionRectangle.Visibility =
            Visibility.Visible;
    }

    private void ImageCanvas_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isSelecting ||
            _loadedBitmap == null)
        {
            return;
        }

        Point currentPoint =
            ClampPointToImage(
                e.GetPosition(
                    ImageCanvas));

        UpdateSelectionRectangle(
            _selectionStart,
            currentPoint);
    }

    private void ImageCanvas_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isSelecting ||
            _loadedBitmap == null)
        {
            return;
        }

        _isSelecting =
            false;

        ImageCanvas.ReleaseMouseCapture();

        Point endPoint =
            ClampPointToImage(
                e.GetPosition(
                    ImageCanvas));

        UpdateSelectionRectangle(
            _selectionStart,
            endPoint);

        int x =
            (int)Math.Round(
                Math.Min(
                    _selectionStart.X,
                    endPoint.X));

        int y =
            (int)Math.Round(
                Math.Min(
                    _selectionStart.Y,
                    endPoint.Y));

        int width =
            (int)Math.Round(
                Math.Abs(
                    endPoint.X -
                    _selectionStart.X));

        int height =
            (int)Math.Round(
                Math.Abs(
                    endPoint.Y -
                    _selectionStart.Y));

        width =
            Math.Min(
                width,
                _loadedBitmap.PixelWidth - x);

        height =
            Math.Min(
                height,
                _loadedBitmap.PixelHeight - y);

        if (width < 2 ||
            height < 2)
        {
            _selectedPixelArea =
                new Int32Rect();

            SelectionRectangle.Visibility =
                Visibility.Collapsed;

            StatusText.Text =
                "Die Auswahl ist zu klein.";

            return;
        }

        _selectedPixelArea =
            new Int32Rect(
                x,
                y,
                width,
                height);

        StatusText.Text =
            $"Auswahl: X={x}, Y={y}, " +
            $"{width} × {height}";
    }

    private void SaveCharacterButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loadedBitmap == null)
        {
            MessageBox.Show(
                "Bitte zuerst ein Bild öffnen.",
                "Kein Bild",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (_selectedPixelArea.IsEmpty)
        {
            MessageBox.Show(
                "Ziehe zuerst ein Rechteck um ein Zeichen.",
                "Keine Auswahl",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        string characterText =
            CharacterTextBox.Text
                .Trim()
                .ToUpperInvariant();

        if (characterText.Length != 1)
        {
            MessageBox.Show(
                "Bitte genau ein Zeichen eingeben.\n\n" +
                "Beispiele: O, P, 3 oder -",
                "Ungültiges Zeichen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        char character =
            characterText[0];

        try
        {
            var croppedBitmap =
                new CroppedBitmap(
                    _loadedBitmap,
                    _selectedPixelArea);

            string templateRoot =
                GetTemplateRootFolder();

            string characterFolderName =
                GetCharacterFolderName(
                    character);

            string characterFolder =
                Path.Combine(
                    templateRoot,
                    characterFolderName);

            Directory.CreateDirectory(
                characterFolder);

            string fileName =
                $"{characterFolderName}_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";

            string outputPath =
                Path.Combine(
                    characterFolder,
                    fileName);

            SavePng(
                croppedBitmap,
                outputPath);

            StatusText.Text =
                $"Template gespeichert: {outputPath}";

            CharacterTextBox.Clear();
            CharacterTextBox.Focus();

            SelectionRectangle.Visibility =
                Visibility.Collapsed;

            _selectedPixelArea =
                new Int32Rect();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "Template konnte nicht gespeichert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateSelectionRectangle(
        Point start,
        Point end)
    {
        double left =
            Math.Min(
                start.X,
                end.X);

        double top =
            Math.Min(
                start.Y,
                end.Y);

        double width =
            Math.Abs(
                end.X -
                start.X);

        double height =
            Math.Abs(
                end.Y -
                start.Y);

        Canvas.SetLeft(
            SelectionRectangle,
            left);

        Canvas.SetTop(
            SelectionRectangle,
            top);

        SelectionRectangle.Width =
            width;

        SelectionRectangle.Height =
            height;
    }

    private Point ClampPointToImage(
        Point point)
    {
        if (_loadedBitmap == null)
        {
            return point;
        }

        double x =
            Math.Clamp(
                point.X,
                0,
                _loadedBitmap.PixelWidth);

        double y =
            Math.Clamp(
                point.Y,
                0,
                _loadedBitmap.PixelHeight);

        return new Point(
            x,
            y);
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

    private static void SavePng(
        BitmapSource bitmap,
        string outputPath)
    {
        var encoder =
            new PngBitmapEncoder();

        encoder.Frames.Add(
            BitmapFrame.Create(
                bitmap));

        using FileStream stream =
            new(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

        encoder.Save(
            stream);
    }

    private static string GetTemplateRootFolder()
    {
        string solutionFolder =
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    @"..\..\..\.."));

        string templateFolder =
            Path.Combine(
                solutionFolder,
                "Data",
                "OCRTemplates");

        Directory.CreateDirectory(
            templateFolder);

        return templateFolder;
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
}