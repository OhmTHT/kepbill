using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Inventory;

/// <summary>A movement as the ledger needs it.</summary>
public sealed record StockLine(DateOnly Date, DateTimeOffset CreatedAt, StockMovementType Type, decimal Quantity, decimal? UnitCost);

/// <param name="Quantity">On hand across every warehouse; negative when more went out than was recorded coming in.</param>
/// <param name="Value">What the quantity on hand cost (baht), at moving average cost.</param>
public readonly record struct StockPosition(decimal Quantity, decimal Value)
{
    public static readonly StockPosition Empty = new(0, 0);

    /// <summary>Cost per unit; 0 when nothing is on hand.</summary>
    public decimal AverageCost => Quantity > 0 ? Value / Quantity : 0;
}

/// <summary>
/// Quantity and value of one product, by moving weighted average (ถัวเฉลี่ยเคลื่อนที่): every receipt of stock
/// re-averages the cost per unit, and stock going out leaves at the average of the moment. Kept free of I/O so the
/// rules can be tested directly.
/// </summary>
public static class StockLedger
{
    /// <summary>Runs the movements in date order (then entry order), so a document entered late still lands in its place.</summary>
    public static StockPosition Run(IEnumerable<StockLine> lines)
    {
        decimal qty = 0, value = 0, lastAverage = 0;
        foreach (var l in lines.OrderBy(l => l.Date).ThenBy(l => l.CreatedAt))
        {
            // A transfer moves stock between warehouses of the same product: the product's total doesn't change.
            if (l.Type is StockMovementType.TransferIn or StockMovementType.TransferOut) continue;

            if (l.Quantity > 0)
            {
                var cost = l.UnitCost ?? lastAverage;
                // Coming in on top of nothing (or of a shortfall), the new stock sets the average by itself.
                value = qty > 0 ? value + l.Quantity * cost : (qty + l.Quantity) * cost;
                qty += l.Quantity;
            }
            else if (l.Quantity < 0)
            {
                qty += l.Quantity;
                value = qty > 0 ? value + l.Quantity * lastAverage : qty * lastAverage;
            }

            if (qty > 0) lastAverage = value / qty;
        }
        return new StockPosition(qty, qty == 0 ? 0 : value);
    }
}
