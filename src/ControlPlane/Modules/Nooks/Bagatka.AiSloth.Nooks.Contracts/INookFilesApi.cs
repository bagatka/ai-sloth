using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Nooks' files (<see cref="INooksApi"/>): downloading them, copying them in and out, checkpoints, the
/// changes in a nook's repositories, and kept folders outside every nook that nooks sync with. Every
/// operation puts the nook's files in place first, and wakes it, except reading its changes.
/// </summary>
public interface INookFilesApi
{
    /// <summary>
    /// Writes the nook's files to <paramref name="destination"/> as a gzipped tar archive: one of its sources,
    /// or all of <c>/work</c>, as they are now or at one of its checkpoints. People who can see the nook
    /// may. Its sources are put in place first. A checkpoint's files are as <see cref="CheckpointAsync"/> keeps them.
    /// </summary>
    /// <returns>
    /// Success; <see cref="NooksErrors.SourceNotFound"/>, also for a source the checkpoint doesn't have;
    /// <see cref="NooksErrors.CheckpointNotFound"/>; <see cref="NooksErrors.NotReady"/>; or not found.
    /// </returns>
    public Task<Result> DownloadAsync(Actor actor, DownloadFiles command, Stream destination, CancellationToken ct);

    /// <summary>
    /// Saves the nook's files as its next checkpoint, kept until the nook is deleted: <c>/work</c> and
    /// every git repository directly in it, with their history and branches, honoring
    /// <c>.gitignore</c>, and the nook's kept paths (<see cref="CreateNook.KeptPaths"/>). Files a
    /// <c>.gitignore</c> leaves out, such as installed dependencies, aren't kept, nor are git's
    /// settings besides remotes. Only what changed since the last checkpoint is stored. Takes a few
    /// seconds, more the first time; files the nook changes meanwhile may be either way. People with
    /// Write may.
    /// </summary>
    /// <returns>The checkpoint; a validation error for a note too long; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result<CheckpointSummary>> CheckpointAsync(Actor actor, CheckpointNook command, CancellationToken ct);

    /// <summary>
    /// Writes the files at the paths, those that exist, to <paramref name="destination"/> as a
    /// gzipped tar archive of names relative to <c>/</c>, for <see cref="CopyFilesInAsync"/>. The same
    /// files make the same bytes: names sorted, without times or owners. People with Write may.
    /// </summary>
    /// <returns>Success; a validation error for paths that aren't absolute; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result> CopyFilesOutAsync(Actor actor, CopyFilesOut command, Stream destination, CancellationToken ct);

    /// <summary>
    /// Unpacks <paramref name="archive"/>, a gzipped tar archive such as <see cref="CopyFilesOutAsync"/>
    /// writes, into the nook at <c>/</c>, replacing the files it names, after removing the paths it
    /// replaces (<see cref="CopyFilesIn.Replacing"/>). Its sources are put in place first. People with
    /// Write may.
    /// </summary>
    /// <returns>Success; a validation error for paths that aren't absolute; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result> CopyFilesInAsync(Actor actor, CopyFilesIn command, Stream archive, CancellationToken ct);

    /// <summary>The nook's checkpoints, newest first. People who can see the nook may.</summary>
    public Task<Result<Page<CheckpointSummary>>> ListCheckpointsAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// What each of the nook's repositories changed since it was copied in, for people to review:
    /// its commits since and what isn't committed, as one patch. The sources stay as they are. It
    /// never wakes the nook, and reading isn't using it: while the nook is awake they're read from it,
    /// otherwise they're the ones its latest checkpoint kept, which a nook takes before it sleeps, so
    /// they're the same. People who can see the nook may.
    /// </summary>
    /// <returns>The changes, a repository's at a time by name; <see cref="NooksErrors.NotReady"/>; or not found.</returns>
    public Task<Result<IReadOnlyList<SourceChanges>>> GetChangesAsync(Actor actor, NookId nookId, CancellationToken ct);

    /// <summary>
    /// Commits what a source has that isn't committed, then writes its commits since it was
    /// copied in to <paramref name="destination"/> as a git bundle, for pushing. People with
    /// Write only. A source without commits beyond its start writes nothing.
    /// </summary>
    /// <returns>
    /// The changes; <see cref="NooksErrors.SourceNotFound"/> for a source that isn't one of its
    /// repositories; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.
    /// </returns>
    public Task<Result<ExportedChanges>> ExportChangesAsync(Actor actor, ExportChanges command, Stream destination, CancellationToken ct);

    /// <summary>
    /// Syncs a folder of the nook with a folder AiSloth keeps outside every nook, so the nooks that sync
    /// the same one share its files, such as an agent's memory across a person's chats: what changed on
    /// either side since the nook last synced comes together. A text file both sides changed keeps the
    /// lines of both, so nothing either wrote is lost; any other file both changed keeps the nook's.
    /// The kept folder starts with the first sync. Only the control plane's own processes may.
    /// </summary>
    /// <returns>Success; a validation error for a path that isn't absolute or a name it can't keep; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result> SyncFolderAsync(Actor actor, SyncFolder command, CancellationToken ct);

    /// <summary>The folders AiSloth keeps whose names start with <paramref name="prefix"/>. Only the control plane's own processes may.</summary>
    public Task<Result<IReadOnlyList<KeptFolderSummary>>> ListFoldersAsync(Actor actor, string prefix, CancellationToken ct);

    /// <summary>
    /// Deletes the folders AiSloth keeps whose names start with <paramref name="prefix"/>, with their
    /// history; nooks keep their own copies of the files. Only the control plane's own processes may.
    /// </summary>
    public Task<Result> DeleteFoldersAsync(Actor actor, string prefix, CancellationToken ct);
}
