using InventoryHold.Contracts.Requests;
using InventoryHold.Domain.Exceptions;

namespace InventoryHold.Domain.Validation;

public static class CreateHoldRequestValidator
{
    public const int MaxLines = 50;
    public const int MaxQuantityPerLine = 1000;

    public static void EnsureValid(CreateHoldRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.CustomerId))
            errors[nameof(request.CustomerId)] = ["A customer ID is required."];

        var items = request.Items ?? [];
        if (items.Count == 0)
            errors[nameof(request.Items)] = ["At least one item is required."];
        else if (items.Count > MaxLines)
            errors[nameof(request.Items)] = [$"A hold can contain at most {MaxLines} items."];
        else
            AddItemErrors(items, errors);

        if (errors.Count > 0)
            throw new HoldValidationException(errors);
    }

    private static void AddItemErrors(IReadOnlyList<HoldItemRequest> items, Dictionary<string, string[]> errors)
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(items[i].Sku))
                problems.Add("A SKU is required.");
            else if (!seen.Add(items[i].Sku))
                problems.Add($"SKU '{items[i].Sku}' appears more than once; combine it into one line.");

            if (items[i].Quantity is < 1 or > MaxQuantityPerLine)
                problems.Add($"Quantity must be between 1 and {MaxQuantityPerLine}.");

            if (problems.Count > 0)
                errors[$"Items[{i}]"] = problems.ToArray();
        }
    }
}
