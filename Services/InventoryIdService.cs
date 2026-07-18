using System;
using System.IO;

namespace OnePieceCardScanner.Services;

public sealed class InventoryIdService
{
    private readonly string _counterFilePath;

    public InventoryIdService()
    {
        _counterFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "inventory-counter.txt");
    }

    public string GenerateNextId()
    {
        int currentNumber = 0;

        if (File.Exists(_counterFilePath))
        {
            string content = File.ReadAllText(_counterFilePath);

            int.TryParse(content, out currentNumber);
        }

        int nextNumber = currentNumber + 1;

        File.WriteAllText(
            _counterFilePath,
            nextNumber.ToString());

        return $"VCV-{nextNumber:000000}";
    }
}