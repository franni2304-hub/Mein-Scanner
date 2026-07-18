using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Recognition;

public interface ICardRecognitionService
{
    RecognitionResult Recognize(string imagePath);
}