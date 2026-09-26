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
        int stock = 20,
        Guid? categoryId = null) =>
        Product.Create(AVenue, name, description, imageUrl, price, stock, categoryId ?? ACategory.Id);

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
            () => Product.Create(Guid.Empty, "Gin Tonic", null, null, 4500m, 20, ACategory.Id));

        Assert.Equal(Product.ErrorCodes.VenueRequired, error.Code);
    }

    // US-14: the tab the customer's menu shows it under.
    [Fact]
    public void Create_WhenValid_KeepsTheCategory()
    {
        Product product = AGinTonic(categoryId: ACategory.Other);

        Assert.Equal(ACategory.Other, product.CategoryId);
    }

    // Whether that category exists in the venue is the handler's question: the
    // domain only refuses to have none at all.
    [Fact]
    public void Create_WhenCategoryIsEmpty_ThrowsCategoryRequired()
    {
        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(categoryId: Guid.Empty));

        Assert.Equal(Product.ErrorCodes.CategoryRequired, error.Code);
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

    // The form shows one error at a time, so the order is what the administrator
    // is told to fix first: the fields in the order they are filled in.
    [Fact]
    public void Create_WhenNameAndStockAreBothInvalid_ThrowsNameRequired()
    {
        DomainException error = Assert.Throws<DomainException>(() => AGinTonic(name: "", stock: -1));

        Assert.Equal(Product.ErrorCodes.NameRequired, error.Code);
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

// US-07: the nightly switch, independent of Create and of Stock.
public class ProductAvailabilityTests
{
    private static Product AGinTonic() =>
        Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20, ACategory.Id);

    [Fact]
    public void MarkUnavailable_WhenAvailable_SetsIsAvailableFalse()
    {
        Product product = AGinTonic();

        product.MarkUnavailable();

        Assert.False(product.IsAvailable);
    }

    // Marking an already-unavailable product unavailable again is not an
    // error: the administrator can click the switch without checking its
    // current state first.
    [Fact]
    public void MarkUnavailable_WhenAlreadyUnavailable_StaysUnavailable()
    {
        Product product = AGinTonic();
        product.MarkUnavailable();

        product.MarkUnavailable();

        Assert.False(product.IsAvailable);
    }

    [Fact]
    public void MarkAvailable_WhenUnavailable_SetsIsAvailableTrue()
    {
        Product product = AGinTonic();
        product.MarkUnavailable();

        product.MarkAvailable();

        Assert.True(product.IsAvailable);
    }

    /// <summary>
    /// Running out is not something the switch can undo. Putting a drink back
    /// on sale with none left would promise the customer something the bar
    /// cannot pour: the only way back is restocking it, which is US-08's job.
    /// </summary>
    [Fact]
    public void MarkAvailable_WhenSoldOut_ThrowsSoldOutCannotBeAvailable()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0, ACategory.Id);

        DomainException error = Assert.Throws<DomainException>(product.MarkAvailable);

        Assert.Equal(Product.ErrorCodes.SoldOutCannotBeAvailable, error.Code);
    }

    // Turning it off is always allowed, sold out included: the administrator
    // taps the switch without first working out what state it was in.
    [Fact]
    public void MarkUnavailable_WhenSoldOut_TurnsTheSwitchOff()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0, ACategory.Id);

        product.MarkUnavailable();

        Assert.False(product.IsAvailable);
    }

    // The rule the customer's menu runs as SQL, owned here.
    [Fact]
    public void IsOrderable_WhenSwitchedOnAndInStock_IsTrue()
    {
        Assert.True(AGinTonic().IsOrderable);
    }

    [Fact]
    public void IsOrderable_WhenSwitchedOff_IsFalse()
    {
        Product product = AGinTonic();
        product.MarkUnavailable();

        Assert.False(product.IsOrderable);
    }

    // Sold out and never switched off by anybody: the switch says yes and the
    // shelf says no, and the shelf wins.
    [Fact]
    public void IsOrderable_WhenSoldOut_IsFalse()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0, ACategory.Id);

        Assert.True(product.IsAvailable);
        Assert.False(product.IsOrderable);
    }
}

public class ProductImageTests
{
    private static Product AGinTonicWithoutPicture() =>
        Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20, ACategory.Id);

    // The picture arrives after the product exists: the upload needs an id to
    // file it under, so creation and the picture are two steps.
    [Fact]
    public void ReplaceImage_WhenThereWasNone_StoresTheAddress()
    {
        Product product = AGinTonicWithoutPicture();

        product.ReplaceImage("https://images.example.com/gin-tonic.jpg");

        Assert.Equal("https://images.example.com/gin-tonic.jpg", product.ImageUrl);
    }

    [Fact]
    public void ReplaceImage_WhenThereWasOne_SwapsIt()
    {
        Product product = AGinTonicWithoutPicture();
        product.ReplaceImage("https://images.example.com/old.jpg");

        product.ReplaceImage("https://images.example.com/new.jpg");

        Assert.Equal("https://images.example.com/new.jpg", product.ImageUrl);
    }

    // Same rule as at creation: it ends up in an img src on a phone.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gin-tonic.jpg")]
    [InlineData("javascript:alert(1)")]
    public void ReplaceImage_WhenTheAddressIsNotAnAbsoluteHttpUrl_ThrowsImageUrlInvalid(string imageUrl)
    {
        Product product = AGinTonicWithoutPicture();

        DomainException error = Assert.Throws<DomainException>(() => product.ReplaceImage(imageUrl));

        Assert.Equal(Product.ErrorCodes.ImageUrlInvalid, error.Code);
        Assert.Null(product.ImageUrl);
    }

}

// US-08: correcting a product. The picture is not here — ReplaceImage already
// covers it, and UploadProductImageHandler reuses it as-is.
public class ProductUpdateTests
{
    private static Product AGinTonic() =>
        Product.Create(Guid.CreateVersion7(), "Gin Tonic", "Gin, tonic and a slice of lime.", null, 4500m, 20, ACategory.Id);

    [Fact]
    public void Update_WhenValid_ReplacesNameDescriptionPriceAndCategory()
    {
        Product product = AGinTonic();

        product.Update("Fernet con Coca", "Medida doble.", 3800m, ACategory.Other);

        Assert.Equal("Fernet con Coca", product.Name);
        Assert.Equal("Medida doble.", product.Description);
        Assert.Equal(3800m, product.Price);
        Assert.Equal(ACategory.Other, product.CategoryId);
    }

    // Same rule as at creation: padding is not part of the name.
    [Fact]
    public void Update_WhenNameHasPadding_TrimsIt()
    {
        Product product = AGinTonic();

        product.Update("  Fernet con Coca  ", null, 3800m, ACategory.Id);

        Assert.Equal("Fernet con Coca", product.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WhenNameIsBlank_ThrowsNameRequiredAndKeepsTheOldOne(string name)
    {
        Product product = AGinTonic();

        DomainException error = Assert.Throws<DomainException>(() => product.Update(name, null, 3800m, ACategory.Id));

        Assert.Equal(Product.ErrorCodes.NameRequired, error.Code);
        Assert.Equal("Gin Tonic", product.Name);
    }

    [Fact]
    public void Update_WhenNameIsLongerThanTheLimit_ThrowsNameLength()
    {
        string tooLong = new('a', Product.NameMaxLength + 1);
        Product product = AGinTonic();

        DomainException error = Assert.Throws<DomainException>(() => product.Update(tooLong, null, 3800m, ACategory.Id));

        Assert.Equal(Product.ErrorCodes.NameLength, error.Code);
    }

    [Fact]
    public void Update_WhenDescriptionIsLongerThanTheLimit_ThrowsDescriptionLengthAndKeepsTheOldOne()
    {
        string tooLong = new('a', Product.DescriptionMaxLength + 1);
        Product product = AGinTonic();

        DomainException error = Assert.Throws<DomainException>(() => product.Update("Fernet con Coca", tooLong, 3800m, ACategory.Id));

        Assert.Equal(Product.ErrorCodes.DescriptionLength, error.Code);
        Assert.Equal("Gin, tonic and a slice of lime.", product.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WhenDescriptionIsBlank_StoresNoDescription(string? description)
    {
        Product product = AGinTonic();

        product.Update("Fernet con Coca", description, 3800m, ACategory.Id);

        Assert.Null(product.Description);
    }

    // US-08, criterion 2 (the half that lives in the domain): a broken price
    // does not touch the price the product had before the attempt.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_WhenPriceIsNotPositive_ThrowsPriceNotPositiveAndKeepsTheOldOne(decimal price)
    {
        Product product = AGinTonic();

        DomainException error = Assert.Throws<DomainException>(() => product.Update("Fernet con Coca", null, price, ACategory.Id));

        Assert.Equal(Product.ErrorCodes.PriceNotPositive, error.Code);
        Assert.Equal(4500m, product.Price);
    }

    // US-14: the correction screen offers the field too.
    [Fact]
    public void Update_WhenCategoryChanges_MovesTheProductToIt()
    {
        Product product = AGinTonic();

        product.Update("Fernet con Coca", null, 3800m, ACategory.Other);

        Assert.Equal(ACategory.Other, product.CategoryId);
    }

    [Fact]
    public void Update_WhenCategoryIsEmpty_ThrowsCategoryRequiredAndKeepsTheOldOne()
    {
        Product product = AGinTonic();

        DomainException error = Assert.Throws<DomainException>(
            () => product.Update("Fernet con Coca", null, 3800m, Guid.Empty));

        Assert.Equal(Product.ErrorCodes.CategoryRequired, error.Code);
        Assert.Equal(ACategory.Id, product.CategoryId);
    }
}

// US-08: taking a product off the menu for good, without losing the row that
// old orders point at.
public class ProductDeactivationTests
{
    private static Product AGinTonic() =>
        Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20, ACategory.Id);

    [Fact]
    public void Deactivate_WhenActive_SetsIsActiveFalse()
    {
        Product product = AGinTonic();

        product.Deactivate();

        Assert.False(product.IsActive);
    }

    [Fact]
    public void Deactivate_WhenAlreadyDeactivated_StaysDeactivated()
    {
        Product product = AGinTonic();
        product.Deactivate();

        product.Deactivate();

        Assert.False(product.IsActive);
    }
}

// Moving the stock by hand: up when a delivery arrives, down when it was loaded
// wrong. Always a change and never a new total, so a sale made meanwhile is
// kept.
public class ProductStockAdjustmentTests
{
    private static Product AGinTonicWith(int stock) =>
        Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, stock, ACategory.Id);

    // The way out of "sold out" that the nightly switch cannot give.
    [Fact]
    public void AdjustStock_WhenSoldOutAndUnitsArrive_IsNoLongerSoldOut()
    {
        Product product = AGinTonicWith(0);

        product.AdjustStock(12);

        Assert.Equal(12, product.Stock);
        Assert.False(product.IsSoldOut);
    }

    [Fact]
    public void AdjustStock_WhenTheChangeIsPositive_AddsToWhatWasLeft()
    {
        Product product = AGinTonicWith(5);

        product.AdjustStock(10);

        Assert.Equal(15, product.Stock);
    }

    // Loaded 200 when it was 20.
    [Fact]
    public void AdjustStock_WhenTheChangeIsNegative_TakesAwayWhatWasLoadedByMistake()
    {
        Product product = AGinTonicWith(200);

        product.AdjustStock(-180);

        Assert.Equal(20, product.Stock);
    }

    [Fact]
    public void AdjustStock_WhenItTakesEverythingAway_IsSoldOut()
    {
        Product product = AGinTonicWith(5);

        product.AdjustStock(-5);

        Assert.True(product.IsSoldOut);
    }

    [Fact]
    public void AdjustStock_WhenItWouldGoBelowZero_ThrowsStockNegativeAndKeepsTheStock()
    {
        Product product = AGinTonicWith(5);

        DomainException error = Assert.Throws<DomainException>(() => product.AdjustStock(-6));

        Assert.Equal(Product.ErrorCodes.StockNegative, error.Code);
        Assert.Equal(5, product.Stock);
    }

    // What the handler asks first: sales between the screen opening and the
    // save can leave less than a correction takes away, and that is expected.
    [Theory]
    [InlineData(5, -5, true)]
    [InlineData(5, -6, false)]
    [InlineData(0, 12, true)]
    public void CanAdjustStock_WhenComparedWithWhatIsLeft_SaysWhetherItFits(int stock, int change, bool fits)
    {
        Assert.Equal(fits, AGinTonicWith(stock).CanAdjustStock(change));
    }

    // A change of nothing is a screen that sent a request it did not need to.
    [Fact]
    public void AdjustStock_WhenTheChangeIsZero_ThrowsStockChangeZero()
    {
        Product product = AGinTonicWith(5);

        DomainException error = Assert.Throws<DomainException>(() => product.AdjustStock(0));

        Assert.Equal(Product.ErrorCodes.StockChangeZero, error.Code);
    }

    // The nightly switch is a separate thing: adjusting does not turn it on.
    [Fact]
    public void AdjustStock_WhenTheSwitchWasOff_LeavesItOff()
    {
        Product product = AGinTonicWith(0);
        product.MarkUnavailable();

        product.AdjustStock(12);

        Assert.False(product.IsAvailable);
    }
}
