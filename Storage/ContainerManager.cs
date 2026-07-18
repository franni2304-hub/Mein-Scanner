using System;
using System.Collections.Generic;
using System.Text;

using OnePieceCardScanner.Models;

namespace OnePieceCardScanner.Storage;

public sealed class ContainerManager
{
    private readonly List<StorageContainer> _containers = new();

    public IReadOnlyList<StorageContainer> Containers => _containers;

    public StorageContainer CreateContainer(
        string name,
        int capacity)
    {
        string code = $"BC-{_containers.Count + 1:000}";

        StorageContainer container = new()
        {
            Code = code,
            Name = name,
            Capacity = capacity
        };

        _containers.Add(container);

        return container;
    }
}