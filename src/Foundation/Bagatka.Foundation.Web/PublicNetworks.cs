using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.Foundation.Web;

/// <summary>
/// Outbound connections to the public internet only: the guard for HTTP clients that call URLs people
/// supply, such as a model endpoint, so they can't reach this host, its private network, or a cloud's
/// metadata service. Use <see cref="ConnectAsync"/> as a <see cref="SocketsHttpHandler.ConnectCallback"/>;
/// every connection is checked after name resolution, redirects included.
/// </summary>
public static class PublicNetworks
{
    /// <summary>
    /// Whether the address is on the public internet: not loopback, private, shared (carrier-grade
    /// NAT), link-local, unique-local, multicast, or reserved.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            bool local = address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast;
            return !local && !address.Equals(IPAddress.IPv6None);
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes switch
        {
            [0, ..] => false,                                   // this network
            [10, ..] => false,                                  // private
            [100, >= 64 and <= 127, ..] => false,               // shared, carrier-grade NAT
            [127, ..] => false,                                 // loopback
            [169, 254, ..] => false,                            // link-local, cloud metadata services
            [172, >= 16 and <= 31, ..] => false,                // private
            [192, 0, 0, _] => false,                            // protocol assignments
            [192, 168, ..] => false,                            // private
            [198, 18 or 19, ..] => false,                       // benchmarking
            [>= 224, ..] => false,                              // multicast and reserved
            _ => true,
        };
    }

    /// <summary>
    /// Resolves the host and connects to its first public address. Fails with an
    /// <see cref="HttpRequestException"/> when the host has none.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        DnsEndPoint target = context.DnsEndPoint;
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(target.Host, cancellationToken).ConfigureAwait(false);

        // Not handled: trying the next public address when the first refuses; the call fails instead.
        IPAddress? address = addresses.FirstOrDefault(IsPublic);
        if (address is null)
        {
            throw new HttpRequestException(target.Host + " has no public address; only public endpoints are allowed.");
        }

        Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, target.Port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
