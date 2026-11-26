using System.Reflection.PortableExecutable;

namespace HousecarlCore;

/// <summary>The numeric file version of a Windows executable, read from its <c>RT_VERSION</c> resource, since <see cref="System.Diagnostics.FileVersionInfo"/> reads none off Windows.</summary>
public static class PeFileVersion
{
    const uint FixedInfoSignature = 0xFEEF04BD;
    const int RtVersion = 16;

    /// <summary>The version as <c>a.b.c.d</c>, or null when the file has none or is no PE.</summary>
    public static string? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var resources = pe.PEHeaders.PEHeader?.ResourceTableDirectory;
            if (resources is not { Size: > 0 }) return null;
            var block = pe.GetSectionData(resources.Value.RelativeVirtualAddress);
            if (block.Length == 0) return null;
            var root = block.GetContent().ToArray();
            var data = Leaf(root, 0, RtVersion);
            if (data is not { } entry) return null;
            var blob = pe.GetSectionData((int)entry.Rva).GetContent(0, (int)entry.Size).ToArray();
            for (int i = 0; i + 20 <= blob.Length; i += 4)
            {
                if (BitConverter.ToUInt32(blob, i) != FixedInfoSignature) continue;
                var ms = BitConverter.ToUInt32(blob, i + 8);
                var ls = BitConverter.ToUInt32(blob, i + 12);
                return $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
        { return null; }
    }

    /// <summary>The first data entry under the <paramref name="typeId"/> subtree: type, then name, then language directories.</summary>
    static (uint Rva, uint Size)? Leaf(byte[] section, int directory, int typeId)
    {
        int Count(int d) => BitConverter.ToUInt16(section, d + 12) + BitConverter.ToUInt16(section, d + 14);
        (uint Id, uint Offset) Entry(int d, int i) => (BitConverter.ToUInt32(section, d + 16 + i * 8), BitConverter.ToUInt32(section, d + 20 + i * 8));
        int at = directory;
        for (int level = 0; level < 3; level++)
        {
            if (Count(at) == 0) return null;
            uint offset = 0;
            for (int i = 0; i < Count(at); i++)
            {
                var (id, off) = Entry(at, i);
                if (level == 0 && (id & 0x80000000) == 0 && id == typeId) { offset = off; break; }
                if (level > 0) { offset = off; break; }
            }
            if (offset == 0) return null;
            at = (int)(offset & 0x7FFFFFFF);
        }
        return (BitConverter.ToUInt32(section, at), BitConverter.ToUInt32(section, at + 4));
    }
}
