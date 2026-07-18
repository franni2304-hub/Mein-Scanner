using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Models;

public sealed class FinancialData
{
    public int Id { get; set; }

    public string InventoryId { get; set; } = string.Empty;

    public decimal? PurchasePrice { get; set; }

    public decimal? EstimatedValue { get; set; }

    public decimal? ListedPrice { get; set; }

    public decimal? SoldPrice { get; set; }

    public decimal? ShippingCost { get; set; }

    public decimal? EbayFees { get; set; }

    public decimal? OtherFees { get; set; }

    public string Currency { get; set; } = "EUR";
}