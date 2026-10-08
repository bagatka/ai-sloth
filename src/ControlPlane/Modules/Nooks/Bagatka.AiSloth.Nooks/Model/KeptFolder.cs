using System;
using System.Collections.Generic;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Model;

// A folder AiSloth keeps outside every nook (KeptFolders), by the name its caller gave it: its
// latest commit, and its history as one bundle in object storage per save, each holding what the
// saves before lack.
internal sealed class KeptFolder
{
    public const int MaxNameLength = 200;

    // Beyond this many saves, the next sync folds the history into one bundle.
    public const int MaxSaves = 20;

    // Used by Keep and by EF: parameter names match property names.
    private KeptFolder(string name, string head, List<string> saves, long bytes, DateTimeOffset savedAt, NookId savedBy)
    {
        Name = name;
        Head = head;
        Saves = saves;
        Bytes = bytes;
        SavedAt = savedAt;
        SavedBy = savedBy;
    }

    public string Name { get; private set; }

    public string Head { get; private set; }

    // The commit each save brought, oldest first; its bundle is at BundleKey.
    public List<string> Saves { get; private set; }

    // The bundles' total size.
    public long Bytes { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    public NookId SavedBy { get; private set; }

    // PostgreSQL's xmin: two nooks saving at once conflict, and the later one syncs again.
    public uint Version { get; private set; }

    public bool Folds => Saves.Count >= MaxSaves;

    public static KeptFolder Keep(string name, string head, long bytes, NookId savedBy, TimeProvider time)
    {
        return new KeptFolder(name, head, [head], bytes, time.GetUtcNow(), savedBy);
    }

    // A nook saved what the folder lacked, or, when it folds, its whole history. Returns the saves
    // whose bundles are no longer needed.
    public List<string> Saved(string head, long bytes, NookId savedBy, TimeProvider time)
    {
        bool folding = Folds;
        List<string> replaced = folding ? Saves : [];
        Saves = folding ? [head] : [.. Saves, head];
        Bytes = folding ? bytes : Bytes + bytes;
        Head = head;
        SavedAt = time.GetUtcNow();
        SavedBy = savedBy;
        return replaced;
    }

    // Where the folder's objects are; each save's are in a folder of their own.
    public static string Prefix(string name)
    {
        return "folders/" + name;
    }

    public static string SavePrefix(string name, string save)
    {
        return Prefix(name) + "/" + save;
    }

    public static string BundleKey(string name, string save)
    {
        return SavePrefix(name, save) + "/bundle";
    }

    public KeptFolderSummary ToSummary()
    {
        return new KeptFolderSummary(Name, SavedAt, Bytes, SavedBy);
    }
}
