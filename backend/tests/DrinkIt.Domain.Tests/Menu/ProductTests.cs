using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Domain.Tests.Menu;

public class ProductTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static Product AGinTonic(
        string name = "Gin Tonic",
        string? description = "Gin, tonic and a slice of lime.",
        string? imageUrl = "https://images.example.com/gin-tonic.jpg",
        decimal price = 4500m,
        int stock = 20) =>
        Product.Create(AVenue, name, description, imageUrl, price, stock);

    [Fact]
    public void Create_WhenValid_StartsActiveAndAvailable()
    {
        Product product = AGinTonic();

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal(AVenue, product.VenueId);
        Assert.Equal("Gin Tonic", product.Name);
        Assert.Equal("Gin, tonic and a slice of lime.", product.Description);
        Assert.Equal("https://images.example.com/gin-tonic.jpg", product.ImageUrl);
        Assert.Equal(4500m, product.Price);
        Assert.Equal(20, product.Stock);
        Assert.True(product.IsAvailable);
        Assert.True(product.IsActive);
    }

    // Products always belong to a venue: two venues have to be able to sell
    // their own "Gin Tonic" without seeing each other's.
    [Fact]
    public void Create_WhenVenueIdIsEmpty_ThrowsVenueRequired()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Product.Create(Guid.Empty, "Gin Tonic", null, null, 4500m, 20));

        Assert.Equal(Product.ErrorCodes.VenueRequired, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsBlank_ThrowsNameRequired(string name)
    {
        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(name: name));

        Assert.Equal(Product.ErrorCodes.NameRequired, error.Code);
    }

    [Fact]
    public void Create_WhenNameIsLongerThanTheLimit_ThrowsNameLength()
    {
        string tooLong = new('a', Product.NameMaxLength + 1);

        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(name: tooLong));

        Assert.Equal(Product.ErrorCodes.NameLength, error.Code);
    }

    // Casing is kept — it is what the customer reads on the menu — but padding
    // is not: "Gin Tonic " and "Gin Tonic" are the same product.
    [Fact]
    public void Create_WhenNameHasPadding_TrimsIt()
    {
        Product product = AGinTonic(name: "  Gin Tonic  ");

        Assert.Equal("Gin Tonic", product.Name);
    }

    // US-06, criterion 2: a price of zero or below is refused with a reason.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-4500)]
    public void Create_WhenPriceIsNotPositive_ThrowsPriceNotPositive(decimal price)
    {
        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(price: price));

        Assert.Equal(Product.ErrorCodes.PriceNotPositive, error.Code);
    }

    [Fact]
    public void Create_WhenStockIsNegative_ThrowsStockNegative()
    {
        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(stock: -1));

        Assert.Equal(Product.ErrorCodes.StockNegative, error.Code);
    }

    // Decided on 2026-09-14: running out of stock sells a product out on its
    // own. The availability switch is a separate thing, for turning a product
    // off while there is still stock.
    [Fact]
    public void Create_WhenStockIsZero_IsSoldOut()
    {
        Product product = AGinTonic(stock: 0);

        Assert.True(product.IsSoldOut);
        Assert.True(product.IsAvailable);
    }

    [Fact]
    public void Create_WhenStockIsPositive_IsNotSoldOut()
    {
        Product product = AGinTonic(stock: 1);

        Assert.False(product.IsSoldOut);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenDescriptionIsBlank_StoresNoDescription(string? description)
    {
        Product product = AGinTonic(description: description);

        Assert.Null(product.Description);
    }

    [Fact]
    public void Create_WhenDescriptionIsLongerThanTheLimit_ThrowsDescriptionLength()
    {
        string tooLong = new('a', Product.DescriptionMaxLength + 1);

        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(description: tooLong));

        Assert.Equal(Product.ErrorCodes.DescriptionLength, error.Code);
    }

    // The image is optional: the menu shows a placeholder in its place.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenImageUrlIsBlank_StoresNoImage(string? imageUrl)
    {
        Product product = AGinTonic(imageUrl: imageUrl);

        Assert.Null(product.ImageUrl);
    }

    // The URL ends up in an <img src> on the customer's phone, so it has to be
    // something a browser can fetch and nothing a browser would execute.
    [Theory]
    [InlineData("gin-tonic.jpg")]
    [InlineData("/images/gin-tonic.jpg")]
    [InlineData("ftp://images.example.com/gin-tonic.jpg")]
    [InlineData("javascript:alert(1)")]
    public void Create_WhenImageUrlIsNotAnAbsoluteHttpUrl_ThrowsImageUrlInvalid(string imageUrl)
    {
        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(imageUrl: imageUrl));

        Assert.Equal(Product.ErrorCodes.ImageUrlInvalid, error.Code);
    }
}
