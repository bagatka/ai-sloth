namespace Bagatka.Foundation;

/// <summary>
/// What people do with the product, and what it leads to, for product analytics. Capturing never
/// waits and never fails the caller, and a host without product analytics drops what it is given.
/// </summary>
public interface IProductEvents
{
    /// <summary>Captures an event.</summary>
    public void Capture(ProductEvent productEvent);
}
