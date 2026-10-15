using Core.Orders;

namespace Core.Tests;

public class OrderTests
{
    [Fact]
    public void Create_adds_an_order_with_increasing_ids()
    {
        IOrderSource source = new LocalOrderSource();
        var a = source.Create("OF-001", 10, out var errA);
        var b = source.Create("OF-002", 5, out _);

        Assert.Null(errA);
        Assert.Equal(1, a!.Id);
        Assert.Equal(2, b!.Id);
        Assert.Equal(new[] { "OF-001", "OF-002" }, source.GetOrders().Select(o => o.Reference));
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("OF-1", 0)]
    [InlineData("OF-1", -3)]
    public void Invalid_input_is_refused_with_a_message(string reference, int quantity)
    {
        var source = new LocalOrderSource();
        Assert.Null(source.Create(reference, quantity, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Empty(source.GetOrders());
    }

    [Fact]
    public void Duplicate_reference_is_refused()
    {
        var source = new LocalOrderSource();
        source.Create("OF-1", 1, out _);
        Assert.Null(source.Create("OF-1", 2, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Select_sets_the_current_order_and_rejects_unknown_ids()
    {
        var source = new LocalOrderSource();
        Assert.Null(source.Selected);
        var order = source.Create("OF-1", 1, out _)!;

        Assert.True(source.Select(order.Id));
        Assert.Equal(order.Id, source.Selected!.Id);
        Assert.False(source.Select(99));
        Assert.Equal(order.Id, source.Selected!.Id);
    }

    [Fact]
    public void Changed_is_raised_on_create_and_select()
    {
        var source = new LocalOrderSource();
        var count = 0;
        source.Changed += () => count++;
        var o = source.Create("OF-1", 1, out _)!;
        source.Select(o.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Orders_and_selection_are_persisted_when_a_path_is_given()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stdmachine-tests-" + Guid.NewGuid());
        var path = Path.Combine(dir, "orders.json");
        try
        {
            var first = new LocalOrderSource(path);
            var o = first.Create("OF-7", 12, out _)!;
            first.Select(o.Id);

            var second = new LocalOrderSource(path);
            Assert.Equal("OF-7", second.Selected!.Reference);
            Assert.Equal(12, second.Selected.Quantity);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
