using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace OnePieceCardScanner.Services;

public sealed class BatchImportService
{
    public List<string> LoadImages(string folderPath)
    {
        List<string> images = new();

        string[] supportedExtensions =
        {
            "*.jpg",
            "*.jpeg",
            "*.png",
            "*.bmp"
        };

        foreach (string extension in supportedExtensions)
        {
            images.AddRange(
                Directory.GetFiles(
                    folderPath,
                    extension,
                    SearchOption.TopDirectoryOnly));
        }

        return images;
    }
}