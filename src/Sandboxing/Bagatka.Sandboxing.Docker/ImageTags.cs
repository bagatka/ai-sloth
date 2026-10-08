using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Sdk.Docker;

namespace Bagatka.Sandboxing.Docker;

// How an engine keeps one release of each image the provider uses, beside what still runs, instead of
// every release it ever pulled or built.
internal static class ImageTags
{
    // Retires the other tags of the reference's repository: each goes once no container uses it.
    // Images of other repositories, and references without a tag, are never touched.
    public static async Task RetireOthersAsync(DockerClient docker, string reference, CancellationToken ct)
    {
        int tagAt = reference.LastIndexOf(':', StringComparison.Ordinal);
        bool tagged = tagAt > reference.LastIndexOf('/', StringComparison.Ordinal) && !reference.Contains('@', StringComparison.Ordinal);
        if (!tagged)
        {
            return;
        }

        string repository = reference[..(tagAt + 1)];
        IReadOnlyList<ImageListItem> images = await docker.ListImagesAsync([], ct);
        List<string> others = [.. images.SelectMany(image => image.RepoTags)
            .Where(tag => tag.StartsWith(repository, StringComparison.Ordinal) && !string.Equals(tag, reference, StringComparison.Ordinal))];
        foreach (string other in others)
        {
            // False when a container still uses it.
            _ = await docker.RemoveImageAsync(other, force: false, ct);
        }
    }
}
