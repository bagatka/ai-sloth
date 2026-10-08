using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// A minimal client for the Docker Engine API (version 1.48, Docker 28 and later) over its Unix socket, covering only the
/// endpoints AiSloth uses. Expected outcomes are results or <see langword="null"/>; anything else the
/// engine rejects throws <see cref="HttpRequestException"/>. Thread-safe.
/// </summary>
public sealed class DockerClient : IDisposable
{
    private const string ApiVersion = "v1.48";

    private readonly HttpClient _http;

    /// <summary>Creates a client for the engine at the settings' endpoint.</summary>
    public DockerClient(DockerClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string socketPath = settings.Endpoint.AbsolutePath;
        SocketsHttpHandler handler = new SocketsHttpHandler
        {
            ConnectCallback = (_, ct) => ConnectAsync(socketPath, ct),
        };

        _http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("http://docker/" + ApiVersion + "/"),

            // Every call takes a cancellation token, and pulling an image can take minutes.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>The names of the OCI runtimes the engine can run containers with, such as <c>runc</c>.</summary>
    public async Task<IReadOnlyList<string>> ListRuntimesAsync(CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(new Uri("info", UriKind.Relative), ct).ConfigureAwait(false);
        DockerWire.SystemInfo body = await ReadAsync(response, DockerJsonContext.Default.SystemInfo, ct).ConfigureAwait(false);
        return body.Runtimes?.Keys.ToList() ?? [];
    }

    /// <summary>The container, or <see langword="null"/> when no container has that name or ID.</summary>
    public async Task<ContainerDetails?> InspectContainerAsync(string nameOrId, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(Path("containers/", nameOrId, "/json"), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        DockerWire.ContainerInspect body = await ReadAsync(response, DockerJsonContext.Default.ContainerInspect, ct).ConfigureAwait(false);
        return new ContainerDetails(
            body.Id,
            body.Name.TrimStart('/'),
            ParseTimestamp(body.Created),
            body.Image,
            body.State.Status,
            body.State.ExitCode,
            body.State.Error ?? string.Empty,
            body.Config?.Env ?? [],
            body.Config?.Labels ?? ReadOnlyDictionary<string, string>.Empty);
    }

    /// <summary>Creates a container without starting it.</summary>
    /// <returns>Its ID; a conflict (<c>docker.name_in_use</c>) when the name is taken; or not found (<c>docker.image_not_found</c>).</returns>
    public async Task<Result<string>> CreateContainerAsync(string name, ContainerConfiguration configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // A lower gateway priority keeps a network from carrying the default route.
        Dictionary<string, DockerWire.EndpointSettings> endpoints = configuration.ExtraNetworks
            .ToDictionary(network => network, _ => new DockerWire.EndpointSettings(GwPriority: -1), StringComparer.Ordinal);
        endpoints[configuration.Network] = new DockerWire.EndpointSettings(GwPriority: 0);
        DockerWire.ContainerCreate body = new DockerWire.ContainerCreate(
            configuration.Image,
            configuration.Environment,
            configuration.Labels,
            configuration.Command.Count > 0 ? configuration.Command : null,
            new DockerWire.HostConfig(
                configuration.NanoCpus,
                configuration.MemoryBytes,
                configuration.ExtraHosts,
                configuration.Runtime,
                configuration.Network,
                configuration.Capabilities,
                configuration.Sysctls),
            new DockerWire.NetworkingConfig(endpoints));

        using JsonContent content = JsonContent.Create(body, DockerJsonContext.Default.ContainerCreate);
        using HttpResponseMessage response = await _http.PostAsync(Path("containers/create?name=", name, string.Empty), content, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            string message = await MessageAsync(response, ct).ConfigureAwait(false);
            return new Result<string>(Error.Conflict("docker.name_in_use", message));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            string message = await MessageAsync(response, ct).ConfigureAwait(false);
            return new Result<string>(Error.NotFound("docker.image_not_found", message));
        }

        DockerWire.IdResponse created = await ReadAsync(response, DockerJsonContext.Default.IdResponse, ct).ConfigureAwait(false);
        return new Result<string>(created.Id);
    }

    /// <summary>
    /// Creates a bridge network on an IPv4 subnet, such as <c>198.18.0.8/29</c>, whose first address
    /// Docker reserves as the gateway, with the driver's options, such as one whose containers can't
    /// reach each other.
    /// </summary>
    /// <returns>
    /// A conflict: <c>docker.network_exists</c> when a network by that name exists already, or
    /// <c>docker.subnet_in_use</c> when another network's subnet overlaps this one.
    /// </returns>
    public async Task<Result> CreateNetworkAsync(
        string name,
        string subnet,
        IReadOnlyDictionary<string, string> options,
        IReadOnlyDictionary<string, string> labels,
        CancellationToken ct)
    {
        DockerWire.NetworkCreate body = new DockerWire.NetworkCreate(name, "bridge", options, labels, new DockerWire.Ipam([new DockerWire.IpamConfig(subnet)]));
        using JsonContent content = JsonContent.Create(body, DockerJsonContext.Default.NetworkCreate);
        using HttpResponseMessage response = await _http.PostAsync(new Uri("networks/create", UriKind.Relative), content, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            string message = await MessageAsync(response, ct).ConfigureAwait(false);
            return new Result(Error.Conflict("docker.network_exists", message));
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            string message = await MessageAsync(response, ct).ConfigureAwait(false);
            return new Result(Error.Conflict("docker.subnet_in_use", message));
        }

        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return new Result(new Success());
    }

    /// <summary>Removes a network.</summary>
    /// <returns><see langword="false"/> when it doesn't exist, or containers are still on it.</returns>
    public async Task<bool> RemoveNetworkAsync(string name, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.DeleteAsync(Path("networks/", name, string.Empty), ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.Forbidden)
        {
            return false;
        }

        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Starts a container. Starting a running container does nothing.</summary>
    public async Task StartContainerAsync(string nameOrId, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsync(Path("containers/", nameOrId, "/start"), content: null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotModified)
        {
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops a container: its entry point gets SIGTERM, and SIGKILL after <paramref name="grace"/>.
    /// Stopping a stopped container does nothing.
    /// </summary>
    public async Task StopContainerAsync(string nameOrId, TimeSpan grace, CancellationToken ct)
    {
        string seconds = ((int)grace.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        using HttpResponseMessage response = await _http.PostAsync(Path("containers/", nameOrId, "/stop?t=" + seconds), content: null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotModified)
        {
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Waits until a container's entry point exits, returning its exit code.</summary>
    public async Task<int> WaitContainerAsync(string nameOrId, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsync(Path("containers/", nameOrId, "/wait"), content: null, ct).ConfigureAwait(false);
        DockerWire.WaitResponse exited = await ReadAsync(response, DockerJsonContext.Default.WaitResponse, ct).ConfigureAwait(false);
        return exited.StatusCode;
    }

    /// <summary>Lets a paused container's processes continue.</summary>
    public async Task UnpauseContainerAsync(string nameOrId, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsync(Path("containers/", nameOrId, "/unpause"), content: null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a container in any state, with its anonymous volumes.
    /// </summary>
    /// <returns><see langword="false"/> when it didn't exist.</returns>
    public async Task<bool> RemoveContainerAsync(string nameOrId, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.DeleteAsync(Path("containers/", nameOrId, "?force=true&v=true"), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>All containers, in any state, that carry every label filter (<c>name</c> or <c>name=value</c>).</summary>
    public async Task<IReadOnlyList<ContainerListItem>> ListContainersAsync(IReadOnlyList<string> labelFilters, CancellationToken ct)
    {
        string filters = Filters(labelFilters, danglingImages: null);
        using HttpResponseMessage response = await _http.GetAsync(Path("containers/json?all=true&filters=", filters, string.Empty), ct).ConfigureAwait(false);
        IReadOnlyList<DockerWire.ContainerSummary> body = await ReadAsync(response, DockerJsonContext.Default.IReadOnlyListContainerSummary, ct).ConfigureAwait(false);
        return body
            .Select(container => new ContainerListItem(
                container.Id,
                container.Labels ?? ReadOnlyDictionary<string, string>.Empty,
                container.State,
                container.Status,
                DateTimeOffset.FromUnixTimeSeconds(container.Created)))
            .ToList();
    }

    /// <summary>Pulls an image, such as <c>registry.k8s.io/pause:3.10</c>, returning when it is present.</summary>
    public async Task PullImageAsync(string reference, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsync(Path("images/create?fromImage=", reference, string.Empty), content: null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        // The engine streams progress as JSON lines and reports failures inside the stream.
        Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using StreamReader reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                DockerWire.PullProgress? progress = JsonSerializer.Deserialize(line, DockerJsonContext.Default.PullProgress);
                if (progress?.Error is { Length: > 0 } error)
                {
                    throw new HttpRequestException("Pulling " + reference + " failed: " + error);
                }
            }
        }
    }

    /// <summary>The image, or <see langword="null"/> when no image has that reference or ID.</summary>
    public async Task<ImageDetails?> InspectImageAsync(string reference, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(Path("images/", reference, "/json"), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        DockerWire.ImageInspect body = await ReadAsync(response, DockerJsonContext.Default.ImageInspect, ct).ConfigureAwait(false);
        return new ImageDetails(
            body.Id,
            body.RepoTags ?? [],
            body.Config?.Env ?? [],
            body.Config?.Labels ?? ReadOnlyDictionary<string, string>.Empty);
    }

    /// <summary>
    /// Commits a container's filesystem as the image <c>repository:tag</c>, pausing it meanwhile.
    /// </summary>
    /// <returns>The new image's ID.</returns>
    public async Task<string> CommitContainerAsync(
        string nameOrId,
        string repository,
        string tag,
        CommitConfiguration configuration,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DockerWire.Commit body = new DockerWire.Commit(configuration.Environment, configuration.Labels);
        string query = "commit?pause=true&container=" + Uri.EscapeDataString(nameOrId)
            + "&repo=" + Uri.EscapeDataString(repository)
            + "&tag=" + Uri.EscapeDataString(tag);

        using JsonContent content = JsonContent.Create(body, DockerJsonContext.Default.Commit);
        using HttpResponseMessage response = await _http.PostAsync(new Uri(query, UriKind.Relative), content, ct).ConfigureAwait(false);
        DockerWire.IdResponse committed = await ReadAsync(response, DockerJsonContext.Default.IdResponse, ct).ConfigureAwait(false);
        return committed.Id;
    }

    /// <summary>All tagged images that carry every label filter (<c>name</c> or <c>name=value</c>).</summary>
    public async Task<IReadOnlyList<ImageListItem>> ListImagesAsync(IReadOnlyList<string> labelFilters, CancellationToken ct)
    {
        string filters = Filters(labelFilters, danglingImages: false);
        using HttpResponseMessage response = await _http.GetAsync(Path("images/json?filters=", filters, string.Empty), ct).ConfigureAwait(false);
        IReadOnlyList<DockerWire.ImageSummary> body = await ReadAsync(response, DockerJsonContext.Default.IReadOnlyListImageSummary, ct).ConfigureAwait(false);
        return body
            .Select(image => new ImageListItem(
                image.Id,
                image.RepoTags ?? [],
                image.Labels ?? ReadOnlyDictionary<string, string>.Empty,
                DateTimeOffset.FromUnixTimeSeconds(image.Created)))
            .ToList();
    }

    /// <summary>
    /// Removes an image reference. Forcing the removal of a tag removes the tag even while containers
    /// use the image; the image itself is deleted only once nothing uses it, and those containers keep
    /// running. Without force, an image that containers use is left in place.
    /// </summary>
    /// <returns><see langword="false"/> when the reference didn't exist, or, without force, when containers still use it.</returns>
    public async Task<bool> RemoveImageAsync(string reference, bool force, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.DeleteAsync(Path("images/", reference, force ? "?force=true" : "?force=false"), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound || (!force && response.StatusCode == HttpStatusCode.Conflict))
        {
            return false;
        }

        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }

    private static async ValueTask<Stream> ConnectAsync(string socketPath, CancellationToken ct)
    {
        Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static Uri Path(string prefix, string value, string suffix)
    {
        return new Uri(prefix + Uri.EscapeDataString(value) + suffix, UriKind.Relative);
    }

    private static string Filters(IReadOnlyList<string> labels, bool? danglingImages)
    {
        Dictionary<string, IReadOnlyList<string>> filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["label"] = labels,
        };

        if (danglingImages is bool dangling)
        {
            filters["dangling"] = [dangling ? "true" : "false"];
        }

        return JsonSerializer.Serialize(filters, DockerJsonContext.Default.DictionaryStringIReadOnlyListString);
    }

    // The engine writes RFC 3339 timestamps with up to nine fractional digits; .NET parses seven.
    private static DateTimeOffset ParseTimestamp(string value)
    {
        int dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            int end = dot + 1;
            while (end < value.Length && char.IsAsciiDigit(value[end]))
            {
                end++;
            }

            string fraction = value[(dot + 1)..end];
            value = value[..(dot + 1)] + (fraction.Length > 7 ? fraction[..7] : fraction) + value[end..];
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        T? body = await response.Content.ReadFromJsonAsync(typeInfo, ct).ConfigureAwait(false);
        if (body is null)
        {
            throw new HttpRequestException("The Docker Engine returned an empty body for " + response.RequestMessage?.RequestUri);
        }

        return body;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = await MessageAsync(response, ct).ConfigureAwait(false);
        throw new HttpRequestException(
            string.Create(CultureInfo.InvariantCulture, $"The Docker Engine returned {(int)response.StatusCode} for {response.RequestMessage?.RequestUri}: {message}"),
            inner: null,
            statusCode: response.StatusCode);
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize(text, DockerJsonContext.Default.ErrorResponse)?.Message ?? text;
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
