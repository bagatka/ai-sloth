using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Sources.Git;

// Directories on this instance's disk where copies of repositories are made, for exports and pushes:
// at most two at once, as a copy is as large as its repository's history, and others wait their turn.
// Each is deleted when its work ends, however it ends.
// Not handled: a repository larger than the disk's free space; its copy fails with git's words.
internal sealed class GitScratch : IDisposable
{
    private readonly SemaphoreSlim _slots = new SemaphoreSlim(2, 2);

    public async Task<Lease> TakeAsync(string purpose, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        bool taken = false;
        try
        {
            Lease lease = new Lease(this, Directory.CreateTempSubdirectory("aisloth-" + purpose + "-"));
            taken = true;
            return lease;
        }
        finally
        {
            if (!taken)
            {
                _slots.Release();
            }
        }
    }

    public void Dispose()
    {
        _slots.Dispose();
    }

    internal sealed class Lease(GitScratch scratch, DirectoryInfo directory) : IDisposable
    {
        public string Path => directory.FullName;

        public void Dispose()
        {
            try
            {
                directory.Delete(recursive: true);
            }
            finally
            {
                scratch._slots.Release();
            }
        }
    }
}
