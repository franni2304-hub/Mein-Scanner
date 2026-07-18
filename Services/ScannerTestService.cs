using OnePieceCardScanner.Recognition;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OnePieceCardScanner.Services;

public sealed class ScannerTestService
{
    private readonly ICardRecognitionService _recognition =
        new CardRecognitionService();

    public string Run(string folder)
    {
        StringBuilder report = new();

        int total = 0;
        int correct = 0;

        foreach (string image in Directory.GetFiles(folder, "*.png"))
        {
            total++;

            string expected =
                Path.GetFileNameWithoutExtension(image);

            RecognitionResult result =
                _recognition.Recognize(image);

            bool ok =
                expected.Equals(
                    result.CardNumber,
                    StringComparison.OrdinalIgnoreCase);

            if (ok)
                correct++;

            report.AppendLine(
                $"{expected} -> {result.CardNumber} {(ok ? "✓" : "✗")}");
        }

        report.AppendLine();
        report.AppendLine($"Treffer: {correct}/{total}");

        return report.ToString();
    }
}
