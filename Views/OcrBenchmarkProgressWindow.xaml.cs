using OnePieceCardScanner.Recognition.OCR;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OnePieceCardScanner.Views;

public partial class OcrBenchmarkProgressWindow : Window
{
    private readonly CancellationTokenSource _cancellation =
        new();

    private string? _reportPath;
    private bool _isFinished;

    public OcrBenchmarkProgressWindow()
    {
        InitializeComponent();

        StatusText.Text =
            "Wähle den Benchmark-Modus und klicke auf „Benchmark starten“.";
    }

    private async void Start_Click(
        object sender,
        RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        BenchmarkModeComboBox.IsEnabled = false;
        CancelButton.Content = "Abbrechen";

        await RunBenchmarkAsync();
    }

    private async Task RunBenchmarkAsync()
    {
        try
        {
            var benchmark =
                new OcrBenchmark();

            var progress =
                new Progress<OcrBenchmarkProgress>(
                    UpdateProgress);

            int? sampleSize =
                GetSelectedSampleSize();

            StatusText.Text =
                GetSelectedStatusText();

            OcrBenchmarkResult result =
                await benchmark.RunAsync(
                    sampleSize,
                    progress,
                    _cancellation.Token);

            _isFinished = true;
            _reportPath = result.ReportPath;

            BenchmarkProgressBar.Value = 100;
            CurrentCardText.Text = "Benchmark abgeschlossen";
            ProgressDetailText.Text =
                $"{result.TestedCards} Karten getestet";

            ResultText.Text =
                $"Richtig: {result.CorrectCards}\n" +
                $"Falsch: {result.FailedCards}\n" +
                $"Kartengenauigkeit: {result.CardAccuracy:0.00} %\n" +
                $"Zeichengenauigkeit: {result.CharacterAccuracy:0.00} %";

            TimeText.Text =
                $"Dauer: {result.Duration:hh\\:mm\\:ss}";

            StatusText.Text =
                $"Report gespeichert unter:\n{result.ReportPath}";

            OpenReportButton.IsEnabled = true;
            CancelButton.Content = "Schließen";
        }
        catch (OperationCanceledException)
        {
            _isFinished = true;

            CurrentCardText.Text =
                "Benchmark abgebrochen";

            StatusText.Text =
                "Es wurde kein vollständiger Report erstellt.";

            CancelButton.IsEnabled = true;
            CancelButton.Content = "Schließen";
        }
        catch (Exception exception)
        {
            _isFinished = true;

            CurrentCardText.Text =
                "Benchmark fehlgeschlagen";

            StatusText.Text =
                exception.ToString();

            CancelButton.IsEnabled = true;
            CancelButton.Content = "Schließen";
        }
    }

    private int? GetSelectedSampleSize()
    {
        return BenchmarkModeComboBox.SelectedIndex switch
        {
            1 => 500,
            2 => 1000,
            3 => null,
            _ => 0
        };
    }

    private string GetSelectedStatusText()
    {
        return BenchmarkModeComboBox.SelectedIndex switch
        {
            1 => "500 Preprocessed-Bilder werden getestet.",
            2 => "1000 Preprocessed-Bilder werden getestet.",
            3 => "Alle Preprocessed-Bilder werden getestet.",
            _ => "Die vorhandenen Benchmark-Trainingsbilder werden getestet."
        };
    }

    private void UpdateProgress(
        OcrBenchmarkProgress progress)
    {
        BenchmarkProgressBar.Value =
            progress.Percent;

        CurrentCardText.Text =
            string.IsNullOrWhiteSpace(
                progress.CurrentCard)
                ? "Benchmark läuft …"
                : $"Aktuelle Karte: {progress.CurrentCard}";

        ProgressDetailText.Text =
            $"{progress.Current} / {progress.Total} " +
            $"({progress.Percent:0.0} %)";

        ResultText.Text =
            $"Richtig: {progress.CorrectCards}\n" +
            $"Falsch: {progress.FailedCards}";

        string remainingText =
            progress.EstimatedRemaining.HasValue
                ? progress.EstimatedRemaining.Value
                    .ToString(@"hh\:mm\:ss")
                : "wird berechnet";

        TimeText.Text =
            $"Vergangen: {progress.Elapsed:hh\\:mm\\:ss}\n" +
            $"Restzeit: {remainingText}";
    }

    private void OpenReport_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _reportPath))
        {
            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = _reportPath,
                UseShellExecute = true
            });
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isFinished)
        {
            Close();
            return;
        }

        _cancellation.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text =
            "Benchmark wird abgebrochen …";
    }

    private void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_isFinished)
        {
            return;
        }

        _cancellation.Cancel();
    }
}
