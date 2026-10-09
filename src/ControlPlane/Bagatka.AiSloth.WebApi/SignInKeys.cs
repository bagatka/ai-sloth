using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Bagatka.AiSloth.WebApi;

// Data Protection's keys, in memory only. They protect a sign-in's browser round trip, which lasts
// minutes, so a restart costs only a sign-in begun before it, which starts again. Data Protection
// would otherwise write them unencrypted to the container's disk, which a restart loses anyway. It
// adds a key about every 90 days of uptime, so the list stays a few keys long.
internal sealed class SignInKeys : IXmlRepository
{
    private readonly Lock _gate = new Lock();
    private readonly List<XElement> _keys = [];

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_gate)
        {
            return [.. _keys.Select(key => new XElement(key))];
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_gate)
        {
            _keys.Add(new XElement(element));
        }
    }
}
