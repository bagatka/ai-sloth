using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Bagatka.Azure.Sandboxes.Models;

namespace Bagatka.Azure.Sandboxes;

/// <summary>
/// The sandboxes and disk images of one sandbox group in Azure Container Apps Sandboxes, through
/// the service's data plane. Thread-safe; create one per group and reuse it.
/// </summary>
/// <remarks>
/// Calls retry throttling and server errors through Azure.Core's pipeline, and fail with
/// <see cref="RequestFailedException"/>, whose <see cref="RequestFailedException.ErrorCode"/> is the
/// service's, such as <c>SandboxNotRunning</c>. Changes the service finishes later, such as a stop,
/// return an <see cref="Operation{T}"/> that polls the resource; pass <see cref="WaitUntil.Completed"/>
/// to wait for it. Methods are virtual so tests can stand in for the client
/// (<see cref="SandboxesModelFactory"/>).
/// </remarks>
public class SandboxGroupClient
{
    private static readonly int[] Ok = [200];
    private static readonly int[] Created = [201];
    private static readonly int[] Deleted = [200, 202, 204];
    private static readonly int[] OkOrMissing = [200, 404];

    private readonly HttpPipeline? _pipeline;
    private readonly Uri? _endpoint;
    private readonly SandboxGroupId? _group;
    private readonly string _version = string.Empty;

    /// <summary>Creates a client for the group, signing in with the credential.</summary>
    /// <param name="endpoint">The service in the group's region (<see cref="GetEndpoint"/>).</param>
    /// <param name="group">The sandbox group.</param>
    /// <param name="credential">
    /// Signs requests in, such as <c>DefaultAzureCredential</c> from Azure.Identity; the identity needs
    /// the <c>Container Apps SandboxGroup Data Owner</c> role on the group.
    /// </param>
    /// <param name="options">The service version and the pipeline's options.</param>
    public SandboxGroupClient(Uri endpoint, SandboxGroupId group, TokenCredential credential, SandboxesClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(credential);
        options ??= new SandboxesClientOptions();
        _pipeline = HttpPipelineBuilder.Build(options, new BearerTokenAuthenticationPolicy(credential, options.Scope));
        _endpoint = endpoint;
        _group = group;
        _version = options.Version;
    }

    /// <summary>For tests that stand in for the client.</summary>
    protected SandboxGroupClient()
    {
    }

    /// <summary>The sandbox group.</summary>
    public virtual SandboxGroupId Group
    {
        get
        {
            if (_group is null)
            {
                throw Mocked();
            }

            return _group;
        }
    }

    private HttpPipeline Pipeline
    {
        get
        {
            if (_pipeline is null)
            {
                throw Mocked();
            }

            return _pipeline;
        }
    }

    private Uri Endpoint
    {
        get
        {
            if (_endpoint is null)
            {
                throw Mocked();
            }

            return _endpoint;
        }
    }

    /// <summary>The service's endpoint in an Azure region, such as <c>eastus2</c>.</summary>
    /// <param name="region">The region the sandbox group is in: lowercase letters and digits.</param>
    public static Uri GetEndpoint(string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        // The region becomes part of the host that receives the credential's tokens.
        if (!region.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character)))
        {
            throw new ArgumentException("A region is lowercase letters and digits, such as eastus2.", nameof(region));
        }

        return new Uri("https://management." + region + ".azuredevcompute.io");
    }

    /// <summary>Creates a sandbox, completed once it runs.</summary>
    public virtual async Task<Operation<Sandbox>> CreateSandboxAsync(WaitUntil waitUntil, SandboxCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return await Telemetry.TraceAsync("CreateSandbox", Endpoint, async () =>
        {
            Wire.CreateSandbox body = new Wire.CreateSandbox(
                new Wire.Source(
                    options.Source.DiskImageId is string image ? new Wire.DiskImageRef(image) : null,
                    options.Source.SnapshotId is string snapshot ? new Wire.SnapshotRef(snapshot) : null),
                new Wire.Resources(options.Resources.Cpu, options.Resources.Memory, options.Resources.Disk),
                options.Labels.Count > 0 ? new Dictionary<string, string>(options.Labels, StringComparer.Ordinal) : null,
                options.Environment.Count > 0 ? new Dictionary<string, string>(options.Environment, StringComparer.Ordinal) : null,
                options.Entrypoint.Count > 0 ? [.. options.Entrypoint] : null,
                options.Command.Count > 0 ? [.. options.Command] : null,
                LifecycleOf(options.AutoSuspend));
            Response<Sandbox> created = await SendAsync(RequestMethod.Post, "/sandboxes", labelSelector: null, Serialize(body, SandboxesJsonContext.Default.CreateSandbox), Created, ReadSandbox, cancellationToken).ConfigureAwait(false);
            return await WaitAsync(waitUntil, SandboxOperation(created.Value.Id, created, done: state => state == SandboxState.Running || state == SandboxState.Idle), cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The sandbox; fails with status 404 when it doesn't exist.</summary>
    public virtual async Task<Response<Sandbox>> GetSandboxAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        return await Telemetry.TraceAsync("GetSandbox", Endpoint, async () =>
        {
            return await SendAsync(RequestMethod.Get, "/sandboxes/" + Uri.EscapeDataString(sandboxId), labelSelector: null, body: null, Ok, ReadSandbox, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The sandbox, or no value when it doesn't exist.</summary>
    public virtual async Task<NullableResponse<Sandbox>> GetSandboxIfExistsAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        return await Telemetry.TraceAsync("GetSandboxIfExists", Endpoint, async () =>
        {
            return await SendAsync(
                RequestMethod.Get,
                "/sandboxes/" + Uri.EscapeDataString(sandboxId),
                labelSelector: null,
                body: null,
                OkOrMissing,
                response => response.Status == 404 ? new MissingResponse<Sandbox>(response) : (NullableResponse<Sandbox>)ReadSandbox(response),
                cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The group's sandboxes that carry every label given, or all of them.</summary>
    public virtual AsyncPageable<Sandbox> GetSandboxesAsync(IReadOnlyDictionary<string, string>? labels = null, CancellationToken cancellationToken = default)
    {
        return new ServicePageable<Sandbox>(
            async (next, ct) => await Telemetry.TraceAsync("GetSandboxes", Endpoint, async () =>
            {
                return await SendPageAsync("/sandboxes", labels, next, response =>
                {
                    Wire.SandboxPage page = Read(response, SandboxesJsonContext.Default.SandboxPage);
                    return Page<Sandbox>.FromValues([.. page.Value.Select(ToModel)], page.NextLink, response);
                }, ct).ConfigureAwait(false);
            }).ConfigureAwait(false),
            cancellationToken);
    }

    /// <summary>
    /// Stops a running sandbox, keeping what its suspend mode says, completed once it is stopped. A
    /// sandbox that isn't running fails with <c>SandboxNotRunning</c>.
    /// </summary>
    public virtual async Task<Operation<Sandbox>> StopSandboxAsync(WaitUntil waitUntil, string sandboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        return await Telemetry.TraceAsync("StopSandbox", Endpoint, async () =>
        {
            await SendAsync(RequestMethod.Post, "/sandboxes/" + Uri.EscapeDataString(sandboxId) + "/stop", labelSelector: null, body: null, Ok, response => response, cancellationToken).ConfigureAwait(false);
            Response<Sandbox> current = await GetSandboxAsync(sandboxId, cancellationToken).ConfigureAwait(false);
            return await WaitAsync(waitUntil, SandboxOperation(sandboxId, current, done: state => state == SandboxState.Stopped), cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes a stopped sandbox, completed once it runs. A sandbox that isn't stopped fails with
    /// <c>InvalidSandboxState</c>.
    /// </summary>
    public virtual async Task<Operation<Sandbox>> ResumeSandboxAsync(WaitUntil waitUntil, string sandboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        return await Telemetry.TraceAsync("ResumeSandbox", Endpoint, async () =>
        {
            Response<Sandbox> resumed = await SendAsync(RequestMethod.Post, "/sandboxes/" + Uri.EscapeDataString(sandboxId) + "/resume", labelSelector: null, body: null, Ok, ReadSandbox, cancellationToken).ConfigureAwait(false);
            return await WaitAsync(waitUntil, SandboxOperation(sandboxId, resumed, done: state => state == SandboxState.Running || state == SandboxState.Idle), cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>Deletes the sandbox; deleting one that doesn't exist succeeds.</summary>
    public virtual async Task<Response> DeleteSandboxAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        return await Telemetry.TraceAsync("DeleteSandbox", Endpoint, async () =>
            await SendAsync(RequestMethod.Delete, "/sandboxes/" + Uri.EscapeDataString(sandboxId), labelSelector: null, body: null, Deleted, response => response, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the sandbox's disk as a disk image that new sandboxes start from with their own labels
    /// and environment, completed once it is ready. The sandbox keeps running.
    /// </summary>
    public virtual async Task<Operation<DiskImage>> CommitSandboxAsync(WaitUntil waitUntil, string sandboxId, IReadOnlyDictionary<string, string>? labels = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        return await Telemetry.TraceAsync("CommitSandbox", Endpoint, async () =>
        {
            Wire.Commit body = new Wire.Commit(labels is { Count: > 0 } ? new Dictionary<string, string>(labels, StringComparer.Ordinal) : null);
            Response<DiskImage> committed = await SendAsync(
                RequestMethod.Post,
                "/sandboxes/" + Uri.EscapeDataString(sandboxId) + "/commit",
                labelSelector: null,
                Serialize(body, SandboxesJsonContext.Default.Commit),
                Ok,
                response => Response.FromValue(ToModel(Read(response, SandboxesJsonContext.Default.CommitResult).DiskImage), response),
                cancellationToken).ConfigureAwait(false);
            return await WaitAsync(waitUntil, DiskImageOperation(committed.Value.Id, committed), cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>Makes a disk image from a container image, completed once it is ready.</summary>
    public virtual async Task<Operation<DiskImage>> CreateDiskImageAsync(WaitUntil waitUntil, DiskImageCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return await Telemetry.TraceAsync("CreateDiskImage", Endpoint, async () =>
        {
            Wire.RegistryAuthentication? authentication = options.RegistryCredentials is RegistryCredentials credentials
                ? new Wire.RegistryAuthentication(new Wire.RegistryCredentials(credentials.Username, credentials.Token))
                : null;
            Wire.CreateDiskImage body = new Wire.CreateDiskImage(
                new Wire.DiskImageSource("registry", options.ImageReference, authentication),
                options.Name,
                options.Labels.Count > 0 ? new Dictionary<string, string>(options.Labels, StringComparer.Ordinal) : null);
            Response<DiskImage> created = await SendAsync(RequestMethod.Post, "/diskimages", labelSelector: null, Serialize(body, SandboxesJsonContext.Default.CreateDiskImage), Created, ReadDiskImage, cancellationToken).ConfigureAwait(false);
            return await WaitAsync(waitUntil, DiskImageOperation(created.Value.Id, created), cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The disk image; fails with status 404 when it doesn't exist.</summary>
    public virtual async Task<Response<DiskImage>> GetDiskImageAsync(string diskImageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskImageId);
        return await Telemetry.TraceAsync("GetDiskImage", Endpoint, async () =>
        {
            return await SendAsync(RequestMethod.Get, "/diskimages/" + Uri.EscapeDataString(diskImageId), labelSelector: null, body: null, Ok, ReadDiskImage, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The disk image, or no value when it doesn't exist.</summary>
    public virtual async Task<NullableResponse<DiskImage>> GetDiskImageIfExistsAsync(string diskImageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskImageId);
        return await Telemetry.TraceAsync("GetDiskImageIfExists", Endpoint, async () =>
        {
            return await SendAsync(
                RequestMethod.Get,
                "/diskimages/" + Uri.EscapeDataString(diskImageId),
                labelSelector: null,
                body: null,
                OkOrMissing,
                response => response.Status == 404 ? new MissingResponse<DiskImage>(response) : (NullableResponse<DiskImage>)ReadDiskImage(response),
                cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The group's disk images that carry every label given, or all of them.</summary>
    public virtual AsyncPageable<DiskImage> GetDiskImagesAsync(IReadOnlyDictionary<string, string>? labels = null, CancellationToken cancellationToken = default)
    {
        return new ServicePageable<DiskImage>(
            async (next, ct) => await Telemetry.TraceAsync("GetDiskImages", Endpoint, async () =>
            {
                return await SendPageAsync("/diskimages", labels, next, response =>
                {
                    Wire.DiskImagePage page = Read(response, SandboxesJsonContext.Default.DiskImagePage);
                    return Page<DiskImage>.FromValues([.. page.Value.Select(ToModel)], page.NextLink, response);
                }, ct).ConfigureAwait(false);
            }).ConfigureAwait(false),
            cancellationToken);
    }

    /// <summary>
    /// Deletes the disk image; deleting one that doesn't exist succeeds. Sandboxes started from it keep
    /// running.
    /// </summary>
    public virtual async Task<Response> DeleteDiskImageAsync(string diskImageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskImageId);
        return await Telemetry.TraceAsync("DeleteDiskImage", Endpoint, async () =>
            await SendAsync(RequestMethod.Delete, "/diskimages/" + Uri.EscapeDataString(diskImageId), labelSelector: null, body: null, Deleted, response => response, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static InvalidOperationException Mocked()
    {
        return new InvalidOperationException("This client was created for a test; override the members it calls.");
    }

    private static async Task<Operation<T>> WaitAsync<T>(WaitUntil waitUntil, Operation<T> operation, CancellationToken ct)
        where T : notnull
    {
        if (waitUntil == WaitUntil.Completed)
        {
            await operation.WaitForCompletionAsync(ct).ConfigureAwait(false);
        }

        return operation;
    }

    private static Wire.Lifecycle? LifecycleOf(SandboxAutoSuspend? autoSuspend)
    {
        if (autoSuspend is null)
        {
            return null;
        }

        int? seconds = autoSuspend.After is TimeSpan after ? (int)after.TotalSeconds : null;
        return new Wire.Lifecycle(new Wire.AutoSuspendPolicy(autoSuspend.Enabled, seconds, autoSuspend.Mode?.ToString()), new Wire.AutoDeletePolicy(Enabled: false));
    }

    private static Sandbox ToModel(Wire.Sandbox sandbox)
    {
        SandboxResources? resources = sandbox.Resources is null ? null : new SandboxResources(sandbox.Resources.Cpu, sandbox.Resources.Memory, sandbox.Resources.Disk);
        return new Sandbox(
            sandbox.Id,
            new SandboxState(sandbox.State ?? string.Empty),
            sandbox.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            resources,
            sandbox.CreatedAt,
            sandbox.SourcesRef?.DiskImage?.Id,
            sandbox.SnapshotId,
            sandbox.Region);
    }

    private static DiskImage ToModel(Wire.DiskImage image)
    {
        return new DiskImage(
            image.Id,
            image.Name,
            image.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            image.Image?.Base,
            new DiskImageState(image.Status?.State ?? string.Empty),
            image.Status?.ErrorMessage is { Length: > 0 } error ? error : null,
            image.Status?.CreatedAt,
            image.SizeInMB);
    }

    private static ReadOnlyMemory<byte> Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
    }

    private static T Read<T>(Response response, JsonTypeInfo<T> typeInfo)
    {
        T? body = JsonSerializer.Deserialize(response.Content.ToMemory().Span, typeInfo);
        if (body is null)
        {
            throw new RequestFailedException(response.Status, "The service answered with an empty body.");
        }

        return body;
    }

    private StateOperation<Sandbox> SandboxOperation(string sandboxId, Response<Sandbox> started, Func<SandboxState, bool> done)
    {
        return new StateOperation<Sandbox>(
            sandboxId,
            started,
            async ct => await GetSandboxAsync(sandboxId, ct).ConfigureAwait(false),
            sandbox => done(sandbox.State),
            sandbox => sandbox.State == SandboxState.StopFailed ? "Stopping sandbox " + sandbox.Id + " failed." : null);
    }

    private StateOperation<DiskImage> DiskImageOperation(string diskImageId, Response<DiskImage> started)
    {
        return new StateOperation<DiskImage>(
            diskImageId,
            started,
            async ct => await GetDiskImageAsync(diskImageId, ct).ConfigureAwait(false),
            image => image.State == DiskImageState.Ready,
            image => image.State == DiskImageState.Failed ? "Making disk image " + image.Id + " failed: " + image.ErrorMessage : null);
    }

    private static Response<Sandbox> ReadSandbox(Response response)
    {
        return Response.FromValue(ToModel(Read(response, SandboxesJsonContext.Default.Sandbox)), response);
    }

    private static Response<DiskImage> ReadDiskImage(Response response)
    {
        return Response.FromValue(ToModel(Read(response, SandboxesJsonContext.Default.DiskImage)), response);
    }

    // A page of a list: the first from the path and labels, the next from the link the last one gave.
    private async Task<T> SendPageAsync<T>(string path, IReadOnlyDictionary<string, string>? labels, string? nextLink, Func<Response, T> read, CancellationToken ct)
    {
        if (nextLink is null)
        {
            string? selector = labels is { Count: > 0 } ? string.Join(",", labels.Select(label => label.Key + "=" + label.Value)) : null;
            return await SendAsync(RequestMethod.Get, path, selector, body: null, Ok, read, ct).ConfigureAwait(false);
        }

        using HttpMessage next = Pipeline.CreateMessage();
        next.Request.Method = RequestMethod.Get;
        next.Request.Uri.Reset(new Uri(nextLink));
        next.Request.Headers.Add("Accept", "application/json");
        return await SendAsync(next, Ok, read, ct).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(RequestMethod method, string path, string? labelSelector, ReadOnlyMemory<byte>? body, int[] expected, Func<Response, T> read, CancellationToken ct)
    {
        using HttpMessage message = Pipeline.CreateMessage();
        Request request = message.Request;
        request.Method = method;
        request.Uri.Reset(Endpoint);
        request.Uri.AppendPath(Group.ToString() + path, escape: false);
        request.Uri.AppendQuery("api-version", _version, escapeValue: true);
        if (labelSelector is not null)
        {
            request.Uri.AppendQuery("labels", labelSelector, escapeValue: true);
        }

        request.Headers.Add("Accept", "application/json");
        if (body is ReadOnlyMemory<byte> content)
        {
            request.Headers.Add("Content-Type", "application/json");
            request.Content = RequestContent.Create(content);
        }

        return await SendAsync(message, expected, read, ct).ConfigureAwait(false);
    }

    // The response is read while its message is alive, which owns its content.
    private async Task<T> SendAsync<T>(HttpMessage message, int[] expected, Func<Response, T> read, CancellationToken ct)
    {
        await Pipeline.SendAsync(message, ct).ConfigureAwait(false);
        Response response = message.Response;
        if (!expected.Contains(response.Status))
        {
            throw new RequestFailedException(response);
        }

        return read(response);
    }
}
