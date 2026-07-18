using System;
using System.IO;
using OpenCvSharp;

namespace OnePieceCardScanner.Recognition;

public static class StarDetector
{
    public static bool Detect(
        string detectedCardPath)
    {
        using var card = Cv2.ImRead(
            detectedCardPath,
            ImreadModes.Color);

        if (card.Empty())
        {
            throw new InvalidOperationException(
                "Die erkannte Karte konnte für die Sternprüfung nicht geladen werden.");
        }

        /*
         * Die Karte ist auf 744 × 1039 Pixel normiert.
         * Der Stern sitzt direkt oberhalb des Raritätsfeldes.
         *
         * Diese Werte sind ein erster, bewusst etwas größerer Bereich.
         */
        const int x = 625;
        const int y = 925;
        const int width = 85;
        const int height = 70;

        Rect region = CreateSafeRegion(
            card,
            x,
            y,
            width,
            height);

        using var cropped =
            new Mat(card, region);

        using var gray = new Mat();

        Cv2.CvtColor(
            cropped,
            gray,
            ColorConversionCodes.BGR2GRAY);

        using var binary = new Mat();

        /*
         * Helle Symbole herauslösen.
         * Der Stern ist normalerweise deutlich heller als sein Umfeld.
         */
        Cv2.Threshold(
            gray,
            binary,
            205,
            255,
            ThresholdTypes.Binary);

        using var kernel =
            Cv2.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(3, 3));

        Cv2.MorphologyEx(
            binary,
            binary,
            MorphTypes.Open,
            kernel,
            iterations: 1);

        Cv2.FindContours(
            binary,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        foreach (Point[] contour in contours)
        {
            double area =
                Math.Abs(
                    Cv2.ContourArea(contour));

            if (area < 20 ||
                area > 900)
            {
                continue;
            }

            Rect bounds =
                Cv2.BoundingRect(contour);

            if (bounds.Width < 5 ||
                bounds.Height < 5)
            {
                continue;
            }

            double ratio =
                (double)bounds.Width /
                bounds.Height;

            /*
             * Ein Stern ist ungefähr quadratisch.
             * Sehr lange Linien und Kartenränder werden so verworfen.
             */
            if (ratio < 0.55 ||
                ratio > 1.65)
            {
                continue;
            }

            double perimeter =
                Cv2.ArcLength(
                    contour,
                    true);

            if (perimeter <= 0)
                continue;

            double circularity =
                4.0 *
                Math.PI *
                area /
                (perimeter * perimeter);

            /*
             * Ein Stern ist gezackt und deshalb weniger kreisförmig.
             * Der Bereich ist absichtlich großzügig.
             */
            if (circularity >= 0.12 &&
                circularity <= 0.72)
            {
                SaveDebugImages(
                    cropped,
                    binary);

                return true;
            }
        }

        SaveDebugImages(
            cropped,
            binary);

        return false;
    }

    private static void SaveDebugImages(
        Mat cropped,
        Mat binary)
    {
        string folder =
            Path.Combine(
                AppContext.BaseDirectory,
                "DebugStar");

        Directory.CreateDirectory(folder);

        Cv2.ImWrite(
            Path.Combine(
                folder,
                "star-region.png"),
            cropped);

        Cv2.ImWrite(
            Path.Combine(
                folder,
                "star-binary.png"),
            binary);
    }

    private static Rect CreateSafeRegion(
        Mat image,
        int x,
        int y,
        int width,
        int height)
    {
        int safeX =
            Math.Clamp(
                x,
                0,
                image.Width - 1);

        int safeY =
            Math.Clamp(
                y,
                0,
                image.Height - 1);

        int safeWidth =
            Math.Clamp(
                width,
                1,
                image.Width - safeX);

        int safeHeight =
            Math.Clamp(
                height,
                1,
                image.Height - safeY);

        return new Rect(
            safeX,
            safeY,
            safeWidth,
            safeHeight);
    }
}