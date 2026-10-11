using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Nooks: where agents work. A nook is an isolated machine with its files and processes, owned by a
/// workspace. Access comes from Workspaces, through the workspace or given for the nook alone: Read
/// sees a nook and watches its processes, and Write creates, deletes, and runs processes. Without
/// access a nook is not found; with too little, forbidden. What runs in a nook is
/// <see cref="INookProcessesApi"/>'s; its files are <see cref="INookFilesApi"/>'s.
/// </summary>
/// <remarks>
/// A nook nobody uses falls asleep after a while (<see cref="NookStatus.Asleep"/>), which is invisible
/// to callers: any operation wakes it first, so callers only notice latency. Processes keep running
/// through a sleep where its provider keeps memory, and otherwise end with exit code -1. A nook's
/// files survive until it is deleted, except when its machine is lost or it sleeps for long: then it
/// starts again from its latest checkpoint, and processes that ran there end with exit code -1.
/// Every operation on a nook's files, and starting a process, puts its files in place first: its
/// repositories copied in, or a checkpoint's files put back, the first time; which can take a while.
/// </remarks>
public interface INooksApi
{
    /// <summary>
    /// Records a nook in the workspace, then asks the chosen sandbox provider to create it. Returns
    /// as soon as it is recorded; a failed provider call is retried by reconciliation, not by the caller.
    /// </summary>
    /// <remarks>
    /// A nook may be created on a provider that isn't available, such as a machine that is offline;
    /// it stays <see cref="NookStatus.Starting"/> until the provider is back.
    /// </remarks>
    /// <returns>
    /// The nook in <see cref="NookStatus.Starting"/>; not found when the actor has no access to the
    /// workspace, or forbidden without Write; or a validation error for a provider the workspace
    /// doesn't have, an image this deployment doesn't offer, repositories or a nook to copy it can't
    /// start with, or kept paths it can't keep.
    /// </returns>
    public Task<Result<NookSummary>> CreateAsync(Actor actor, CreateNook command, CancellationToken ct);

    /// <summary>
    /// The providers the workspace's nooks can run on: the deployment's own, then the workspace's
    /// machines, oldest first. Not found when the actor has no access to the workspace.
    /// </summary>
    public Task<Result<IReadOnlyList<ProviderSummary>>> ListProvidersAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// The nook. Not found when it doesn't exist or the actor has no access to it.
    /// </summary>
    public Task<Result<NookSummary>> GetAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// The workspace's nooks, newest first. Not found when the actor has no access to the workspace,
    /// such as a guest of one of its nooks.
    /// </summary>
    public Task<Result<Page<NookSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Marks the nook <see cref="NookStatus.Deleting"/> and asks its provider to delete it, with its
    /// files. Deleting a nook that is already being deleted succeeds.
    /// </summary>
    public Task<Result> DeleteAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// The nook's setup (<see cref="NookSetup"/>). Its files are put in place first, which starts its
    /// setup when it has one. People who can see the nook may.
    /// </summary>
    /// <returns>The setup; <see cref="NooksErrors.NotReady"/>; or not found.</returns>
    public Task<Result<NookSetup>> GetSetupAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// Runs the nook's setup again now, as its scripts are now, as when the nook got its files: to
    /// check that it is safe and fast to run again. People with Write may.
    /// </summary>
    /// <returns>The setup with the new run; <see cref="NooksErrors.SetupRunning"/>; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result<NookSetup>> RunSetupAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// Wakes the nook when it sleeps, without waiting for it, and counts as a use: it falls asleep only
    /// once its sleep period has passed since. Whatever keeps it busy, such as Chats while its agent
    /// works, says for how long and renews it; a person about to use it, such as by opening its chat,
    /// says nothing. People with Write may.
    /// </summary>
    /// <returns>Success; <see cref="NooksErrors.NotReady"/> when its provider can't be asked; a validation error for longer than an hour; or not found or forbidden.</returns>
    public Task<Result> WakeAsync(Actor actor, WakeNook command, CancellationToken ct);
}
