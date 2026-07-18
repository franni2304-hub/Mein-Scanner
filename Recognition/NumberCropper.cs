using OpenCvSharp;

namespace OnePieceCardScanner.Recognition;

public static class NumberCropper
{
    public static Mat Crop(Mat image)
    {
        int width = image.Width;
        int height = image.Height;

        // Nur den Bereich mit der Kartennummer behalten.
        Rect roi = new Rect(
            (int)(width * 0.58),
            (int)(height * 0.05),
            (int)(width * 0.28),
            (int)(height * 0.70));

        return new Mat(image, roi).Clone();
    }
}