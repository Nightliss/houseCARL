using System.Collections.Concurrent;

namespace HousecarlCore;

// Where a Data-relative path (backslash-separated, any case) meets a disk that may be case-sensitive.

/// <summary>Data-relative path pieces and the one join onto a disk folder. A Data path is spelled with <c>\</c> and any
/// casing; on Windows the disk accepts both, elsewhere the join maps the separator and each segment to the casing on disk.</summary>
public static class DiskPath
{
    /// <summary>A path Mutagen hands back, in Data-path spelling: Mutagen joins segments with the OS separator, houseCARL's Data paths use <c>\</c>.</summary>
    public static string Logical(string path) => path.Replace('/', '\\');

    /// <summary>Is this a Windows drive or UNC rooted path (<c>C:\x</c>, <c>\\host\x</c>)? <see cref="Path.IsPathRooted(string)"/> says no to those off Windows.</summary>
    public static bool IsDriveRooted(string path) =>
        path.Length >= 2 && ((char.IsAsciiLetter(path[0]) && path[1] == ':') || (path[0] is '\\' or '/' && path[1] is '\\' or '/'));

    /// <summary>Does this argument name a file on disk rather than a Data path? On Windows: fully qualified. Elsewhere: drive or UNC rooted, or a leading <c>/</c> whose first segment is a folder at the filesystem root (<c>/tmp</c>, <c>/home</c>; <c>/meshes</c> is not), since a Data path may also be spelled with a leading slash.</summary>
    public static bool NamesDiskPath(string path)
    {
        if (OperatingSystem.IsWindows()) return Path.IsPathFullyQualified(path);
        if (path.Length == 0) return false;
        return IsDriveRooted(path) || (path[0] == '/' && Directory.Exists('/' + path.TrimStart('/').Split('/')[0]));
    }

    /// <summary>A folder that exists or is a link whose target is gone: Windows reports a dangling junction as a folder, Unix as nothing.</summary>
    public static bool DirectoryOrDanglingLink(string path) => Directory.Exists(path) || new DirectoryInfo(path).LinkTarget is not null;

    /// <summary>Directory.EnumerateFiles' own defaults (hidden and system entries kept, inaccessible ones throw) with the Windows case-insensitive pattern match, which Unix does not apply by default.</summary>
    public static EnumerationOptions IgnoreCase { get; } = new() { MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Win32, AttributesToSkip = 0, IgnoreInaccessible = false };

    /// <summary>The Data path's last segment.</summary>
    public static string FileName(string logical) => logical[(logical.AsSpan().LastIndexOfAny('\\', '/') + 1)..];

    /// <summary>The Data path without its last segment, or empty when it has one segment.</summary>
    public static string DirectoryName(string logical)
    {
        var i = logical.AsSpan().LastIndexOfAny('\\', '/');
        return i < 0 ? "" : logical[..i];
    }

    /// <summary>The last segment's extension with its dot, or empty.</summary>
    public static string Extension(string logical) => Path.GetExtension(FileName(logical));

    /// <summary>The last segment without its extension.</summary>
    public static string FileNameWithoutExtension(string logical) => Path.GetFileNameWithoutExtension(FileName(logical));

    /// <summary>Join <paramref name="logical"/> onto <paramref name="folder"/>. On Windows that is <see cref="Path.Combine(string, string)"/>;
    /// elsewhere each segment is matched to the entry of that name in any case, and a segment with no match is kept as written.</summary>
    public static string Join(string folder, string logical)
    {
        if (OperatingSystem.IsWindows() || logical.Length == 0) return Path.Combine(folder, logical);
        var current = folder;
        var segments = logical.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            var actual = Match(current, segments[i]);
            if (actual is null)
            {
                for (; i < segments.Length; i++) current = Path.Combine(current, segments[i]);
                return current;
            }
            current = Path.Combine(current, actual);
        }
        return current;
    }

    /// <summary>The name in <paramref name="dir"/> that equals <paramref name="name"/> ignoring case: the exact spelling when
    /// it exists, else the first match in ordinal order; null when none, or the folder does not list.</summary>
    static string? Match(string dir, string name)
    {
        var exact = Path.Combine(dir, name);
        if (Path.Exists(exact)) return name;
        try
        {
            var stamp = Directory.GetLastWriteTimeUtc(dir).Ticks;
            if (!Folded.TryGetValue(dir, out var held) || held.Stamp != stamp)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal))
                    map.TryAdd(entry!, entry!);
                Folded[dir] = held = (stamp, map);
            }
            return held.Names.TryGetValue(name, out var found) ? found : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Folded name tables by folder, each valid while the folder's write time is the one it was read at.</summary>
    static readonly ConcurrentDictionary<string, (long Stamp, Dictionary<string, string> Names)> Folded = new();
}
