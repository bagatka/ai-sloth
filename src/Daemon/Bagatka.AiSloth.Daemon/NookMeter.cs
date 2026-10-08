using System;
using System.Globalization;
using System.IO;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// The memory and CPU the nook's processes use, read from the nook's cgroup where it has one, as in a
/// container, and otherwise from the machine's own figures, as in a virtual machine. A figure that
/// can't be read is 0. CPU is measured between two readings, so the first reports none.
/// </summary>
internal sealed class NookMeter(TimeProvider time)
{
    private const string Cgroup = "/sys/fs/cgroup";

    private long _lastCpuMicroseconds = -1;
    private long _lastTimestamp;

    /// <summary>The memory in use, without the file cache the kernel frees when it needs to, and the memory the nook may use.</summary>
    public static (long Used, long Total) Memory()
    {
        // memory.max says "max" for no limit, which reads as none.
        long? limit = Parse(ReadText(Path.Combine(Cgroup, "memory.max")));
        long? current = Parse(ReadText(Path.Combine(Cgroup, "memory.current")));
        long? cache = ReadField(Path.Combine(Cgroup, "memory.stat"), "inactive_file");
        long total = limit ?? (ReadField("/proc/meminfo", "MemTotal:") ?? 0) * 1024;
        long used = current is long inCgroup
            ? inCgroup - (cache ?? 0)
            : total - ((ReadField("/proc/meminfo", "MemAvailable:") ?? 0) * 1024);
        return (Math.Max(used, 0), total);
    }

    /// <summary>The CPU used since the last reading, and the CPU the nook may use, in thousandths of a core.</summary>
    public (int Used, int Total) Cpu()
    {
        // cpu.max is "<quota> <period>" in microseconds, or "max <period>" for no limit.
        string[] quota = (ReadText(Path.Combine(Cgroup, "cpu.max")) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        long? allowed = quota.Length == 2 ? Parse(quota[0]) : null;
        long? period = quota.Length == 2 ? Parse(quota[1]) : null;
        int total = Environment.ProcessorCount * 1000;
        if (allowed is long microseconds && period is long every && every > 0)
        {
            total = (int)(microseconds * 1000 / every);
        }

        long? usage = ReadField(Path.Combine(Cgroup, "cpu.stat"), "usage_usec");
        long now = time.GetTimestamp();
        int used = 0;
        if (usage is long spent && _lastCpuMicroseconds >= 0)
        {
            double elapsed = time.GetElapsedTime(_lastTimestamp, now).TotalMicroseconds;
            used = elapsed > 0 ? (int)((spent - _lastCpuMicroseconds) * 1000 / elapsed) : 0;
        }

        _lastCpuMicroseconds = usage ?? -1;
        _lastTimestamp = now;
        return (Math.Max(used, 0), total);
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? Parse(string? text)
    {
        bool parsed = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number);
        return parsed ? number : null;
    }

    // The number after the key in a file of "<key> <number>" lines, such as memory.stat or /proc/meminfo.
    private static long? ReadField(string path, string key)
    {
        string? text = ReadText(path);
        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[0], key, StringComparison.Ordinal))
            {
                return Parse(parts[1]);
            }
        }

        return null;
    }
}
