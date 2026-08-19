using Inventory.Api.Domain;

namespace Inventory.UnitTests;

public sealed class ProductTests
{
    [Fact]
    public void CreateNormalizesCodeAndDescription()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

        var product = Product.Create("  abc-01  ", "  Mechanical Keyboard  ", 10, now);

        Assert.Equal("ABC-01", product.Code);
        Assert.Equal("Mechanical Keyboard", product.Description);
        Assert.Equal(10, product.Balance);
        Assert.Equal(now, product.CreatedAt);
    }

    [Fact]
    public void CreateRejectsNegativeBalance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Product.Create("ABC", "Product", -1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DebitDecreasesBalanceWithoutGoingNegative()
    {
        var product = Product.Create("ABC", "Product", 3, DateTimeOffset.UtcNow);

        product.Debit(3);

        Assert.Equal(0, product.Balance);
        Assert.Throws<InvalidOperationException>(() => product.Debit(1));
    }
}
