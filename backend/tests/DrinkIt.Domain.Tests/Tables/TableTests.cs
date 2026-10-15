using DrinkIt.Domain.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Domain.Tests.Tables;

public class TableTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    private static Table TableFive(int number = 5, int capacity = 8, string? location = null) =>
        Table.Create(AVenue, number, capacity, location, Now);

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
        DomainException error = Assert.Throws<DomainException>(() => Table.Create(Guid.Empty, 5, 8, null, Now));

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

    [Fact]
    public void Create_WhenValid_KeepsHowManyPeopleItSeatsAndWhereItIs()
    {
        Table table = TableFive(capacity: 10, location: "  Next to the DJ booth  ");

        Assert.Equal(10, table.Capacity);
        Assert.Equal("Next to the DJ booth", table.Location);
    }

    // The venue knows its tables by number; where they sit is a hint, not a must.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenLocationIsBlank_LeavesItEmpty(string? location)
    {
        Assert.Null(TableFive(location: location).Location);
    }

    [Fact]
    public void Create_WhenLocationIsTooLong_ThrowsLocationLength()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => TableFive(location: new string('a', Table.LocationMaxLength + 1)));

        Assert.Equal(Table.ErrorCodes.LocationLength, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Table.CapacityMaxValue + 1)]
    public void Create_WhenCapacityIsOutOfRange_ThrowsCapacityOutOfRange(int capacity)
    {
        DomainException error = Assert.Throws<DomainException>(() => TableFive(capacity: capacity));

        Assert.Equal(Table.ErrorCodes.CapacityOutOfRange, error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Table.CapacityMaxValue)]
    public void Create_WhenCapacityIsAtTheEdgeOfTheRange_Accepts(int capacity)
    {
        Assert.Equal(capacity, TableFive(capacity: capacity).Capacity);
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
