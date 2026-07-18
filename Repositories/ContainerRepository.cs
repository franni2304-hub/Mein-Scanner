using System;
using System.Collections.Generic;
using System.Text;
using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Repositories;

public sealed class ContainerRepository
{
    private readonly List<StorageContainer> _containers = new();

    public IReadOnlyList<StorageContainer> GetAll()
    {
        return _containers;
    }

    public void Add(StorageContainer container)
    {
        _containers.Add(container);
    }
}