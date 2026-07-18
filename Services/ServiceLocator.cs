using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Services;

public static class ServiceLocator
{
    public static LocalCardDatabaseService Database { get; }
        = new();
}