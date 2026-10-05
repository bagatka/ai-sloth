namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Which networks the model gateway may forward agents' calls to. Each call goes to its chat's agent
/// account's endpoint, which people may choose, so by default only public https endpoints are reached.
/// </summary>
internal sealed record ModelGatewaySettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="allowPrivateNetworks">
    /// Whether endpoints may also be on this host or a private network, and use plain HTTP: for a
    /// company's own model endpoint inside its network, a local model, or tests. Off by default.
    /// </param>
    public ModelGatewaySettings(bool allowPrivateNetworks = false)
    {
        AllowPrivateNetworks = allowPrivateNetworks;
    }

    /// <summary>Whether endpoints may be on this host or a private network, and use plain HTTP.</summary>
    public bool AllowPrivateNetworks { get; }
}
