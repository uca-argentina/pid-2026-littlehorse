using DrinkIt.Domain.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Domain.Tests.Tables;

public class TableTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    private static Table TableFive(int number = 5) => Table.Create(AVenue, number, Now);

    [Fact]
    public void Create_WhenValid_KeepsTheVenueTheNumberAndTheMoment()
    {
        Table table = TableFive();

        Assert.NotEqual(Guid.Empty, table.Id);
        Assert.Equal(AVenue, table.VenueId);
        Assert.Equal(5, table.Number);
        Assert.Equal(Now, table.CreatedAt);
    }

    [Fact]
    public void Create_WhenValid_StartsActive()
    {
        Assert.True(TableFive().IsActive);
    }

    // Two venues each have their own "Mesa 5", so a table cannot exist outside one.
    [Fact]
    public void Create_WhenVenueIdIsEmpty_ThrowsVenueRequired()
    {
        DomainException error = Assert.Throws<DomainException>(() => Table.Create(Guid.Empty, 5, Now));

        Assert.Equal(Table.ErrorCodes.VenueRequired, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Table.NumberMaxValue + 1)]
    public void Create_WhenNumberIsOutOfRange_ThrowsNumberOutOfRange(int number)
    {
        DomainException error = Assert.Throws<DomainException>(() => TableFive(number));

        Assert.Equal(Table.ErrorCodes.NumberOutOfRange, error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Table.NumberMaxValue)]
    public void Create_WhenNumberIsAtTheEdgeOfTheRange_Accepts(int number)
    {
        Assert.Equal(number, TableFive(number).Number);
    }

    // The row stays: orders already placed from it keep saying "Mesa 5".
    [Fact]
    public void Deactivate_WhenActive_LeavesItInactiveWithItsNumber()
    {
        Table table = TableFive();

        table.Deactivate();

        Assert.False(table.IsActive);
        Assert.Equal(5, table.Number);
    }

    [Fact]
    public void Activate_WhenDeactivated_PutsItBack()
    {
        Table table = TableFive();
        table.Deactivate();

        table.Activate();

        Assert.True(table.IsActive);
    }
}
