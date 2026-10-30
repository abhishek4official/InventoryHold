namespace InventoryHold.Domain.Exceptions;

/// <summary>A business rule rejected the request. The Web API maps each subtype to an HTTP status.</summary>
public abstract class DomainException(string message) : Exception(message);

public sealed class HoldValidationException(IReadOnlyDictionary<string, string[]> errors)
    : DomainException("The hold request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class HoldNotFoundException(string holdId)
    : DomainException($"Hold '{holdId}' does not exist.");

public sealed class ProductNotFoundException(IReadOnlyCollection<string> skus)
    : DomainException($"Unknown SKU(s): {string.Join(", ", skus)}.")
{
    public IReadOnlyCollection<string> Skus { get; } = skus;
}

public sealed class InsufficientStockException(string sku, int requested)
    : DomainException($"Not enough stock for SKU '{sku}' to hold {requested}.")
{
    public string Sku { get; } = sku;
    public int Requested { get; } = requested;
}

public sealed class HoldAlreadyReleasedException(string holdId)
    : DomainException($"Hold '{holdId}' has already been released.");

public sealed class HoldExpiredException(string holdId)
    : DomainException($"Hold '{holdId}' has expired and its stock has been returned.");
