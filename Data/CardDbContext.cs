using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Data;

public sealed class CardDbContext : DbContext
{
    public DbSet<CollectionItem> CollectionItems =>
        Set<CollectionItem>();

    public DbSet<ImportSession> ImportSessions =>
        Set<ImportSession>();

    public DbSet<StorageContainer> StorageContainers =>
        Set<StorageContainer>();

    public DbSet<FinancialData> FinancialData =>
        Set<FinancialData>();

    public DbSet<InventoryHistory> InventoryHistory =>
        Set<InventoryHistory>();

    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder)
    {
        string applicationDataPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VoidCenturyVault");

        Directory.CreateDirectory(applicationDataPath);

        string databasePath = Path.Combine(
            applicationDataPath,
            "void-century-vault.db");

        optionsBuilder
    .LogTo(Console.WriteLine)
    .EnableSensitiveDataLogging()
    .UseSqlite($"Data Source={databasePath}");
    }
}