using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace OnePieceCardScanner.Core;

public static class ApplicationPaths
{
    public static string RootFolder { get; } =
        @"D:\VoidCenturyVault";

    public static string DatabaseFolder =>
        Path.Combine(RootFolder, "Database");

    public static string ImagesFolder =>
        Path.Combine(RootFolder, "Images");

    public static string OriginalImagesFolder =>
        Path.Combine(ImagesFolder, "Original");

    public static string CroppedImagesFolder =>
        Path.Combine(ImagesFolder, "Cropped");

    public static string ThumbnailFolder =>
        Path.Combine(ImagesFolder, "Thumbnails");

    public static string BackupFolder =>
        Path.Combine(RootFolder, "Backups");

    public static void CreateFolders()
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(DatabaseFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(OriginalImagesFolder);
        Directory.CreateDirectory(CroppedImagesFolder);
        Directory.CreateDirectory(ThumbnailFolder);
        Directory.CreateDirectory(BackupFolder);
    }
}
