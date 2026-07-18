using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Recognition.OCR.CardNumberRecognition;

public sealed class CardNumberRecognitionResult
{
    public string RecognizedText { get; set; } = "";

    public string CorrectedCardNumber { get; set; } = "";

    public bool FoundInDatabase { get; set; }

    public double Score { get; set; }

    public override string ToString()
    {
        if (FoundInDatabase)
        {
            return $"{CorrectedCardNumber} ({Score:0.0}%)";
        }

        return $"{RecognizedText} ({Score:0.0}%)";
    }
}