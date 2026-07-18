using OnePieceCardScanner.Services;
using OnePieceCardScanner.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace OnePieceCardScanner.Views;

public partial class InventoryWindow : Window
{
    public InventoryWindow()
    {
        InitializeComponent();

        LoadInventory();
    }

    private void LoadInventory()
    {
        var inventoryService = new InventoryService();

        InventoryGrid.ItemsSource =
            inventoryService.GetInventory();
    }
    private void InventoryGrid_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
    {
        if (InventoryGrid.SelectedItem is not InventoryItemViewModel item)
        {
            CardPreviewText.Text = "Keine Karte ausgewählt";
            CardPreviewImage.Source = null;
            return;
        }

        CardPreviewText.Text = item.Name;

        if (string.IsNullOrWhiteSpace(item.ImageUrl))
        {
            CardPreviewImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(
                item.ImageUrl,
                UriKind.RelativeOrAbsolute);
            bitmap.EndInit();
            bitmap.Freeze();

            CardPreviewImage.Source = bitmap;
        }
        catch
        {
            CardPreviewImage.Source = null;
            CardPreviewText.Text =
                $"{item.Name}\nBild konnte nicht geladen werden";
        }
    }
}


