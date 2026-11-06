using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DIPlaybox.Core;

public enum ArchiveKind { StandardZip, DisneyInfinity }
public sealed record ArchiveItem(string Name, long Size, long PackedSize, string Method, uint? Crc, long Offset = -1);

public sealed class ArchiveService : IDisposable
{
    private const int MaxItemBytes = 512 * 1024 * 1024;
    private const int MaxEntries = 100_000;
    private static readonly byte[] Key = { 0x68,0x1b,0xbe,0xea,0x63,0x16,0x01,0x88,0xf9,0xb7,0x94,0x51,0x04,0xa5,0x14,0x99 };
    public string PathName { get; }
    public ArchiveKind Kind { get; }
    public IReadOnlyList<ArchiveItem> Items { get; }
    private readonly FileStream file;
    private readonly ZipArchive? zip;
    private readonly object ioLock = new();

    public ArchiveService(string path)
    {
        PathName = Path.GetFullPath(path);
        file = File.Open(PathName, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (file.Length < 8) throw new InvalidDataException("File is too small to be a supported archive.");
            byte[] start = ReadExact(file, 8);
            if (IsStandardZip(start))
            {
                Kind = ArchiveKind.StandardZip;
                file.Position = 0;
                zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
                if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("Too many entries.");
                Items = zip.Entries.Select(e => new ArchiveItem(e.FullName.Replace('\\','/'), e.Length, e.CompressedLength, "ZIP", null)).ToList();
            }
            else
            {
                Kind = ArchiveKind.DisneyInfinity;
                byte[] h = Ctr(start);
                if (h[0] != 'P' || h[1] != 'K' || h[2] != 255 || h[3] != 255)
                    throw new InvalidDataException("Not a standard ZIP or supported Disney Infinity archive.");
                uint count = BitConverter.ToUInt32(h, 4);
                if (count > MaxEntries || 8L + count * 8L > file.Length) throw new InvalidDataException("Invalid DI archive table.");
                file.Position = 0;
                byte[] table = Ctr(ReadExact(file, checked(8 + (int)count * 8)));
                var items = new List<ArchiveItem>((int)count);
                for (int i = 0; i < count; i++)
                {
                    // 8-byte table record: CRC/hash (4) + local-header offset (4).
                    long offset = BitConverter.ToUInt32(table, 12 + i * 8);
                    if (offset < 0 || offset + 30 > file.Length) throw new InvalidDataException($"Invalid entry offset at {i}.");
                    file.Position = offset;
                    byte[] local = Ctr(ReadExact(file, 30));
                    if (BitConverter.ToUInt32(local, 0) != 0x04034b50) throw new InvalidDataException($"Invalid local ZIP header at {offset:X}.");
                    int nameLength = BitConverter.ToUInt16(local, 26);
                    int extraLength = BitConverter.ToUInt16(local, 28);
                    int prefix = checked(30 + nameLength + extraLength);
                    uint packed = BitConverter.ToUInt32(local, 18);
                    uint size = BitConverter.ToUInt32(local, 22);
                    ushort method = BitConverter.ToUInt16(local, 8);
                    uint crc = BitConverter.ToUInt32(local, 14);
                    if (size > MaxItemBytes || packed > MaxItemBytes || offset + prefix + packed > file.Length)
                        throw new InvalidDataException("Entry exceeds limits or archive bounds.");
                    file.Position = offset;
                    byte[] metadata = Ctr(ReadExact(file, prefix));
                    string name = Encoding.UTF8.GetString(metadata, 30, nameLength).Replace('\\', '/');
                    items.Add(new ArchiveItem(name, size, packed, method switch { 0 => "Stored", 8 => "Deflate", _ => $"Method {method}" }, crc, offset));
                }
                Items = items;
            }
        }
        catch { zip?.Dispose(); file.Dispose(); throw; }
    }

    public static bool IsStandardZip(ReadOnlySpan<byte> h) => h.Length >= 4 && h[0] == 0x50 && h[1] == 0x4b &&
        ((h[2] == 3 && h[3] == 4) || (h[2] == 5 && h[3] == 6) || (h[2] == 7 && h[3] == 8));

    public byte[] Read(ArchiveItem item)
    {
        if (!Items.Contains(item)) throw new ArgumentException("Entry does not belong to this archive.");
        if (item.Size > MaxItemBytes) throw new InvalidDataException("Entry exceeds size limit.");
        lock (ioLock)
        {
            if (Kind == ArchiveKind.StandardZip)
            {
                var entry = zip!.GetEntry(item.Name) ?? throw new FileNotFoundException("Entry missing", item.Name);
                using var input = entry.Open();
                return ReadExact(input, checked((int)item.Size));
            }
            file.Position = item.Offset;
            byte[] local = Ctr(ReadExact(file, 30));
            int prefix = 30 + BitConverter.ToUInt16(local, 26) + BitConverter.ToUInt16(local, 28);
            int packed = checked((int)item.PackedSize);
            file.Position = item.Offset;
            int first = Math.Min(512, packed);
            byte[] decoded = Ctr(ReadExact(file, checked(prefix + first)));
            byte[] payload = new byte[packed];
            Buffer.BlockCopy(decoded, prefix, payload, 0, first);
            byte[] rest = ReadExact(file, packed - first);
            Buffer.BlockCopy(rest, 0, payload, first, rest.Length);
            byte[] result;
            if (item.Method == "Stored") result = payload;
            else if (item.Method == "Deflate")
            {
                using var ms = new MemoryStream(payload);
                using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                result = ReadExact(ds, checked((int)item.Size));
                if (ds.ReadByte() != -1) throw new InvalidDataException("Expanded size exceeds header.");
            }
            else throw new NotSupportedException($"Compression {item.Method} is not supported yet.");
            if (result.LongLength != item.Size || Crc32(result) != item.Crc)
                throw new InvalidDataException($"CRC/size mismatch: {item.Name}");
            return result;
        }
    }

    public void Extract(IEnumerable<ArchiveItem> selected, string destination, IProgress<int>? progress = null)
    {
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var list = selected.ToList();
        long total = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            string name = item.Name.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(x => x == ".."))
                throw new InvalidDataException($"Unsafe path: {name}");
            string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Unsafe path: {name}");
            if (name.EndsWith('/')) Directory.CreateDirectory(target);
            else
            {
                total += item.Size;
                if (total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Extraction exceeds 4 GiB safety limit.");
                byte[] bytes = Read(item);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
            progress?.Report((i + 1) * 100 / Math.Max(1, list.Count));
        }
    }

    public void Dispose() { zip?.Dispose(); file.Dispose(); }

    private static byte[] ReadExact(Stream s, int count)
    {
        if (count < 0 || count > MaxItemBytes) throw new InvalidDataException("Entry size exceeds limit.");
        byte[] bytes = new byte[count];
        int p = 0, n;
        while (p < count && (n = s.Read(bytes, p, count - p)) > 0) p += n;
        if (p != count) throw new EndOfStreamException("Unexpected end of archive.");
        return bytes;
    }

    // Matches the original DI3Archives.cs: zero IV, AES block encryption, big-endian counter increment.
    private static byte[] Ctr(byte[] input)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var transform = aes.CreateEncryptor();
        byte[] counter = new byte[16], block = new byte[16], result = new byte[input.Length];
        for (int p = 0; p < input.Length; p += 16)
        {
            transform.TransformBlock(counter, 0, 16, block, 0);
            for (int j = 0; j < 16 && p + j < input.Length; j++) result[p + j] = (byte)(input[p + j] ^ block[j]);
            for (int j = 15; j >= 0; j--) if (++counter[j] != 0) break;
        }
        return result;
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xffffffff;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }
}
