using Microsoft.EntityFrameworkCore;
using OnePieceCardScanner.Data;
using OnePieceCardScanner.Models;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace OnePieceCardScanner.Repositories;

public sealed class CollectionItemRepository
{
    public void Add(CollectionItem item)
    {
        using CardDbContext db = new();

        db.CollectionItems.Add(item);

        db.SaveChanges();
    }

    public List<CollectionItem> GetAll()
    {
        using CardDbContext db = new();

        return db.CollectionItems
                 .OrderBy(item => item.InventoryId)
                 .ToList();
    }

    public bool ExistsByScanPath(string scanPath)
    {
        using CardDbContext db = new();

        return db.CollectionItems.Any(item =>
            item.OriginalScanPath == scanPath);
    }
}