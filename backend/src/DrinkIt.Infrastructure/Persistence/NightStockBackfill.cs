namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// US-37: gives every night that already exists its own stock rows, from the
/// number each product carried before nights owned the stock.
/// </summary>
/// <remarks>
/// <para>
/// <c>Products.Stock</c> was already net of every sale, so a product's chain
/// has to end there: the first night is loaded with what is left now plus
/// everything the nights sold, and each next night starts with what the last
/// one left. What a night sold is what its orders say, canceled ones aside:
/// their units went back to the shelf.
/// </para>
/// <para>
/// Idempotent, so running it twice (the migration and its test) leaves one row
/// per night and product. Orders with no night predate nights altogether and
/// were never sold from one.
/// </para>
/// </remarks>
internal static class NightStockBackfill
{
    // 7 is OrderStatus.Canceled. The numbers are stored and nobody may renumber
    // them, which is what makes it safe to write it here.
    public const string Sql = """
        WITH Sales AS (
            SELECT o.NightId, i.ProductId, SUM(i.Quantity) AS Units
            FROM Orders o
            JOIN OrderItems i ON i.OrderId = o.Id
            WHERE o.NightId IS NOT NULL AND o.Status <> 7
            GROUP BY o.NightId, i.ProductId
        ),
        Grid AS (
            SELECT n.VenueId, n.Id AS NightId, n.StartsAt, p.Id AS ProductId, p.Stock,
                   COALESCE(s.Units, 0) AS Sold
            FROM Nights n
            JOIN Products p ON p.VenueId = n.VenueId
            LEFT JOIN Sales s ON s.NightId = n.Id AND s.ProductId = p.Id
        ),
        Chain AS (
            SELECT g.VenueId, g.NightId, g.ProductId, g.Stock, g.Sold,
                   SUM(g.Sold) OVER (PARTITION BY g.ProductId) AS TotalSold,
                   SUM(g.Sold) OVER (
                       PARTITION BY g.ProductId
                       ORDER BY g.StartsAt
                       ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS SoldBefore
            FROM Grid g
        )
        INSERT INTO NightStocks (Id, VenueId, NightId, ProductId, Loaded, Remaining)
        SELECT NEWID(), c.VenueId, c.NightId, c.ProductId,
               c.Stock + c.TotalSold - COALESCE(c.SoldBefore, 0),
               c.Stock + c.TotalSold - COALESCE(c.SoldBefore, 0) - c.Sold
        FROM Chain c
        WHERE NOT EXISTS (
            SELECT 1 FROM NightStocks x
            WHERE x.NightId = c.NightId AND x.ProductId = c.ProductId);
        """;
}
