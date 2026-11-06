using System.Text;
using System.Globalization;

namespace DIPlaybox.Core;

// Record grammar reviewed against TupleDecoderResearch/decode_tup_safe.py.
// Research provenance: zzh8829/JiroTheOne decode-tup.py, Disney-Infinity-Modding-Tools.
public sealed record BentRecord(int Offset, int Depth, int Type, string Name, string Value);
public sealed class BentDocument
{
    public List<BentRecord> Records { get; } = new();
    public string Status { get; private set; } = "";
    public static BentDocument Read(byte[] data)
    {
        if (data.Length > 64 * 1024 * 1024) throw new InvalidDataException("BENT limit: 64 MiB.");
        var result = new BentDocument();
        using var reader = new BinaryReader(new MemoryStream(data), new UTF8Encoding(false, true));
        int p() => checked((int)reader.BaseStream.Position);
        string Str()
        {
            int start = p(), end = Array.IndexOf(data, (byte)0, start);
            if (end < 0 || end - start > 1048576) throw new InvalidDataException("Invalid BENT string.");
            reader.BaseStream.Position = end + 1;
            return new UTF8Encoding(false, true).GetString(data, start, end - start);
        }
        if (data.Length < 64 || !data.AsSpan(0,4).SequenceEqual(new byte[]{0x29,0x76,1,0x45}))
            throw new InvalidDataException("Unsupported BENT signature; this reader supports the reviewed little-endian tuple variant.");
        reader.BaseStream.Position = 61;
        var strings = new List<string>{""};
        while (true)
        {
            string value = Str(); strings.Add(value);
            if (value == "\x01") break;
            if (strings.Count > 65536) throw new InvalidDataException("Too many BENT strings.");
        }
        if (reader.ReadBytes(2).Length != 2) throw new InvalidDataException("Truncated BENT preamble.");
        string Ref() { int i = reader.ReadUInt16(); return i < strings.Count ? strings[i] : throw new InvalidDataException("Invalid BENT string index."); }
        string N(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        string ArrayValues(int count, Func<string> read)
        {
            var values = new List<string>();
            for (int i=0;i<count;i++) { string value=read(); if(i<128) values.Add(value); }
            return "[" + string.Join(", ", values) + (count>128?$", … ({count} values)":"") + "]";
        }
        int previous = 0;
        while (p() < data.Length)
        {
            int offset = p();
            try
            {
                if (result.Records.Count >= 100000) throw new InvalidDataException("Record display limit reached.");
                int flag = reader.ReadUInt16(), depth = flag / 1024, type = flag % 1024;
                string name = Ref(), value;
                if (depth < 1 || depth > previous + 1 || depth > 128) throw new InvalidDataException("Invalid/overdeep BENT hierarchy.");
                previous = depth;
                int Count(bool wide) => wide ? reader.ReadUInt16() : reader.ReadByte();
                string U24() => N(reader.ReadByte() | reader.ReadByte()<<8 | reader.ReadByte()<<16);
                switch (type)
                {
                    case 1: value=""; break;
                    case 5: case 11: value=Ref(); break;
                    case 10: case 74: value=ArrayValues(Count(type==74),Ref); break;
                    case 0x13: value=N(reader.ReadSingle()); break;
                    case 0x1b: value=N(reader.ReadSByte()); break;
                    case 0x11b: value=N(reader.ReadUInt16()); break;
                    case 0x21b: value=U24(); break;
                    case 0x31b: value=N(reader.ReadUInt32()); break;
                    case 0x12: case 0x52: value=ArrayValues(Count(type==0x52),()=>N(reader.ReadSingle())); break;
                    case 0x1a: value=ArrayValues(Count(false),()=>N(reader.ReadSByte())); break;
                    case 0x23: case 0x5a: value=ArrayValues(Count(type==0x5a),()=>N(reader.ReadByte())); break;
                    case 0x11a: case 0x15a: value=ArrayValues(Count(type==0x15a),()=>N(reader.ReadUInt16())); break;
                    case 0x21a: value=ArrayValues(Count(false),U24); break;
                    case 0x16: case 0x56: value=Ref()+" "+ArrayValues(Count(type==0x56),()=>N(reader.ReadSingle())); break;
                    case 0x35e: value=Ref()+" "+ArrayValues(Count(true),()=>N(reader.ReadUInt32())); break;
                    case 0x63:
                        int length=reader.ReadUInt16(); var bytes=reader.ReadBytes(length);
                        if(bytes.Length!=length) throw new EndOfStreamException();
                        value=$"{length} raw bytes: "+Convert.ToHexString(bytes.AsSpan(0,Math.Min(64,length))); break;
                    default: throw new InvalidDataException($"Unsupported record type 0x{type:X}");
                }
                result.Records.Add(new(offset,depth,type,name,value));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            { result.Status=$"Partial inspection: stopped at 0x{offset:X}: {ex.Message}"; return result; }
        }
        result.Status=$"Decoded {result.Records.Count:N0} records; consumed {data.Length:N0} bytes. No animation evaluation or editing.";
        return result;
    }
}

