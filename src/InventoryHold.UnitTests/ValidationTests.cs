using InventoryHold.Contracts.Requests;
using InventoryHold.Domain.Exceptions;
using InventoryHold.Domain.Validation;

namespace InventoryHold.UnitTests;

[TestFixture]
public class ValidationTests
{
    private static HoldValidationException Invalid(CreateHoldRequest request) =>
        Assert.Throws<HoldValidationException>(() => CreateHoldRequestValidator.EnsureValid(request))!;

    [Test]
    public void Valid_request_passes() =>
        Assert.DoesNotThrow(() => CreateHoldRequestValidator.EnsureValid(
            new CreateHoldRequest("c1", [new HoldItemRequest("A", 1), new HoldItemRequest("B", 1000)])));

    [TestCase("")]
    [TestCase("   ")]
    public void Blank_customer_is_rejected(string customerId) =>
        Assert.That(Invalid(new CreateHoldRequest(customerId, [new HoldItemRequest("A", 1)])).Errors,
            Does.ContainKey(nameof(CreateHoldRequest.CustomerId)));

    [Test]
    public void Empty_items_are_rejected() =>
        Assert.That(Invalid(new CreateHoldRequest("c1", [])).Errors,
            Does.ContainKey(nameof(CreateHoldRequest.Items)));

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(CreateHoldRequestValidator.MaxQuantityPerLine + 1)]
    public void Out_of_range_quantity_is_rejected(int quantity) =>
        Assert.That(Invalid(new CreateHoldRequest("c1", [new HoldItemRequest("A", quantity)])).Errors,
            Does.ContainKey("Items[0]"));

    [Test]
    public void Blank_sku_is_rejected() =>
        Assert.That(Invalid(new CreateHoldRequest("c1", [new HoldItemRequest(" ", 1)])).Errors,
            Does.ContainKey("Items[0]"));

    [Test]
    public void Duplicate_sku_is_rejected() =>
        Assert.That(Invalid(new CreateHoldRequest("c1", [new HoldItemRequest("A", 1), new HoldItemRequest("A", 2)])).Errors,
            Does.ContainKey("Items[1]"));

    [Test]
    public void Too_many_lines_are_rejected()
    {
        var items = Enumerable.Range(0, CreateHoldRequestValidator.MaxLines + 1)
            .Select(i => new HoldItemRequest($"S{i}", 1)).ToList();

        Assert.That(Invalid(new CreateHoldRequest("c1", items)).Errors,
            Does.ContainKey(nameof(CreateHoldRequest.Items)));
    }

    [Test]
    public void Every_problem_is_reported_at_once()
    {
        var errors = Invalid(new CreateHoldRequest("", [new HoldItemRequest("", 0)])).Errors;

        Assert.Multiple(() =>
        {
            Assert.That(errors, Does.ContainKey(nameof(CreateHoldRequest.CustomerId)));
            Assert.That(errors["Items[0]"], Has.Length.EqualTo(2));
        });
    }
}
