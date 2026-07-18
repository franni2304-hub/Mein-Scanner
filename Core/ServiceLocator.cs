using System;
using System.Collections.Generic;
using System.Text;
using OnePieceCardScanner.Repositories;
using OnePieceCardScanner.Services;
using OnePieceCardScanner.Storage;

namespace OnePieceCardScanner.Core;

public static class ServiceLocator
{
    public static readonly InventoryIdService InventoryIdService =
        new();

    public static readonly InventoryManager InventoryManager =
        new();

    public static readonly CardDatabaseService CardDatabase =
        new();

    public static readonly StorageManager StorageManager =
        new();

    public static readonly ContainerManager ContainerManager =
        new();

    public static readonly ContainerRepository ContainerRepository =
        new();
}