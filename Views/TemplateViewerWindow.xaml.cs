using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OnePieceCardScanner.Views;

public partial class TemplateViewerWindow : Window
{
    private readonly string _templateFolder;

    public TemplateViewerWindow()
    {
        InitializeComponent();

        string solutionFolder =
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    @"..\..\..\.."));

        _templateFolder =
            Path.Combine(
                solutionFolder,
                "Data",
                "OCRTemplates");

        Directory.CreateDirectory(
            _templateFolder);

        FolderText.Text =
            _templateFolder;

        LoadCharacterFolders();
    }

    private void LoadCharacterFolders()
    {
        CharacterList.Items.Clear();
        TemplateList.Items.Clear();

        string[] folders =
            Directory.GetDirectories(
                _templateFolder);

        List<CharacterFolderItem> items =
            folders
                .Select(folder =>
                    new CharacterFolderItem
                    {
                        Name =
                            Path.GetFileName(folder),

                        FolderPath =
                            folder,

                        TemplateCount =
                            Directory.GetFiles(
                                folder,
                                "*.png",
                                SearchOption.TopDirectoryOnly)
                            .Length
                    })
                .OrderBy(item =>
                    GetCharacterOrder(item.Name))
                .ThenBy(item =>
                    item.Name)
                .ToList();

        foreach (CharacterFolderItem item in items)
        {
            CharacterList.Items.Add(
                item);
        }

        int totalTemplates =
            items.Sum(item =>
                item.TemplateCount);

        StatusText.Text =
            $"Zeichenklassen: {items.Count} | " +
            $"Templates insgesamt: {totalTemplates}";

        if (CharacterList.Items.Count > 0)
        {
            CharacterList.SelectedIndex = 0;
        }
        else
        {
            SelectedCharacterText.Text =
                "Noch keine Templates vorhanden";
        }
    }

    private void LoadTemplates(
        CharacterFolderItem folder)
    {
        TemplateList.Items.Clear();

        string[] imageFiles =
            Directory.GetFiles(
                folder.FolderPath,
                "*.png",
                SearchOption.TopDirectoryOnly);

        foreach (string imagePath in imageFiles
                     .OrderBy(path =>
                         Path.GetFileName(path)))
        {
            TemplateList.Items.Add(
                new TemplateImageItem
                {
                    FilePath =
                        imagePath,

                    FileName =
                        Path.GetFileName(imagePath),

                    Image =
                        LoadBitmap(imagePath)
                });
        }

        SelectedCharacterText.Text =
            $"Zeichen: {folder.Name} — " +
            $"{imageFiles.Length} Templates";

        StatusText.Text =
            $"Aktuelle Zeichenklasse: {folder.Name} | " +
            $"Templates: {imageFiles.Length}";
    }

    private static BitmapImage LoadBitmap(
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

    private void CharacterList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (CharacterList.SelectedItem
            is not CharacterFolderItem folder)
        {
            return;
        }

        LoadTemplates(folder);
    }

    private void TemplateList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (TemplateList.SelectedItem
            is not TemplateImageItem item)
        {
            return;
        }

        OpenPath(
            item.FilePath);
    }

    private void OpenFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenPath(
            _templateFolder);
    }

    private void Refresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadCharacterFolders();
    }

    private void DeleteSelectedTemplate_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem
            is not TemplateImageItem item)
        {
            MessageBox.Show(
                "Bitte zuerst ein Template auswählen.",
                "Kein Template ausgewählt",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        MessageBoxResult result =
            MessageBox.Show(
                $"Soll dieses Template wirklich gelöscht werden?\n\n" +
                $"{item.FileName}",
                "Template löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(
                item.FilePath);

            if (CharacterList.SelectedItem
                is CharacterFolderItem folder)
            {
                LoadTemplates(folder);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Template konnte nicht gelöscht werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void OpenPath(
        string path)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Datei konnte nicht geöffnet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static int GetCharacterOrder(
        string character)
    {
        const string order =
            "0123456789OPEBSTR-DASH";

        if (string.Equals(
                character,
                "Dash",
                StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        int index =
            order.IndexOf(
                character,
                StringComparison.OrdinalIgnoreCase);

        return index < 0
            ? 200
            : index;
    }

    private sealed class CharacterFolderItem
    {
        public string Name { get; init; } =
            string.Empty;

        public string FolderPath { get; init; } =
            string.Empty;

        public int TemplateCount { get; init; }

        public override string ToString()
        {
            return
                $"{Name} ({TemplateCount})";
        }
    }

    private sealed class TemplateImageItem
    {
        public string FilePath { get; init; } =
            string.Empty;

        public string FileName { get; init; } =
            string.Empty;

        public BitmapImage? Image { get; init; }
    }
}