using System;
using System.Collections.Generic;
using System.Globalization;
using Bagatka.Foundation;
using Bagatka.PostHog;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Product events as PostHog events of the host's project: the person by their ID, the workspace as
/// the event's <c>workspace</c> group, and the facts as properties.
/// </summary>
internal sealed class ProductEventsToPostHog(PostHogClient postHog) : IProductEvents
{
    public void Capture(ProductEvent productEvent)
    {
        PostHogEvent captured = new PostHogEvent(productEvent.Name, productEvent.Person.Value.ToString("D", CultureInfo.InvariantCulture));
        if (productEvent.Workspace is Guid workspace)
        {
            captured.Groups["workspace"] = workspace.ToString("D", CultureInfo.InvariantCulture);
        }

        foreach (KeyValuePair<string, ProductFact> fact in productEvent.Facts)
        {
            captured.Properties[fact.Key] = fact.Value switch
            {
                string text => text,
                double number => number,
                bool flag => flag,
            };
        }

        postHog.Capture(captured);
    }
}
