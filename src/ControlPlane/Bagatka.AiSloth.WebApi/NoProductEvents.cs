using Bagatka.Foundation;

namespace Bagatka.AiSloth.WebApi;

/// <summary>Product events of a host without product analytics: dropped.</summary>
internal sealed class NoProductEvents : IProductEvents
{
    public void Capture(ProductEvent productEvent)
    {
    }
}
