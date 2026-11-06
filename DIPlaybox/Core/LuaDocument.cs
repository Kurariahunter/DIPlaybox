using System.Globalization;
using System.Text;

namespace DIPlaybox.Core;

public sealed record LuaDocument(string Kind, string Notice, string Source, string Instructions, string Constants, bool IsBytecode);

/// <summary>Parses Lua 5.1 data; never loads a VM or executes a chunk.</summary>
public static class LuaDecoder
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    private const int OutputLimit = 8 * 1024 * 1024;
    private static readonly string[] Opcodes = "MOVE LOADK LOADBOOL LOADNIL GETUPVAL GETGLOBAL GETTABLE SETGLOBAL SETUPVAL SETTABLE NEWTABLE SELF ADD SUB MUL DIV MOD POW UNM NOT LEN CONCAT JMP EQ LT LE TEST TESTSET CALL TAILCALL RETURN FORLOOP FORPREP TFORLOOP SETLIST CLOSE CLOSURE VARARG".Split(' ');

    private sealed class Bytes : IEquatable<Bytes>
    {
        public byte[] Value { get; }
        public Bytes(byte[] value) => Value = value;
        public bool Equals(Bytes? other) => other != null && Value.AsSpan().SequenceEqual(other.Value);
        public override bool Equals(object? other) => other is Bytes s && Equals(s);
        public override int GetHashCode() { var hash = new HashCode(); foreach (byte b in Value) hash.Add(b); return hash.ToHashCode(); }
    }
    private sealed class Table : Dictionary<object, object?> { }
    private sealed record Prototype(string Source, uint[] Code, object?[] Constants, Prototype[] Children);

    private sealed class Reader
    {
        private readonly byte[] bytes;
        private int offset, instructions, functions;
        private readonly bool little;
        private readonly int stringSize, numberSize;
        public Reader(byte[] bytes)
        {
            this.bytes = bytes;
            if (bytes.Length < 12) throw new InvalidDataException("Truncated Lua header.");
            if (bytes[4] != 0x51) throw new NotSupportedException($"Lua version 0x{bytes[4]:X2} is not supported; this decoder handles Lua 5.1.");
            if (bytes[5] != 0 || bytes[6] > 1 || bytes[7] != 4 || (bytes[8] != 4 && bytes[8] != 8) || bytes[9] != 4 || (bytes[10] != 4 && bytes[10] != 8) || bytes[11] != 0)
                throw new NotSupportedException("Unsupported Lua 5.1 header layout (requires 32-bit instructions/integers and floating-point numbers).");
            little = bytes[6] == 1; stringSize = bytes[8]; numberSize = bytes[10]; offset = 12;
        }
        private byte[] Take(int length)
        {
            if (length < 0 || length > bytes.Length - offset) throw new InvalidDataException("Lua chunk ends before its declared data.");
            var result = bytes.AsSpan(offset, length).ToArray(); offset += length; return result;
        }
        private ulong Unsigned(int size)
        {
            var data = Take(size); ulong value = 0;
            for (int i = 0; i < size; i++) value |= (ulong)data[i] << (8 * (little ? i : size - i - 1));
            return value;
        }
        private int Count()
        {
            ulong value = Unsigned(4);
            if (value > 1_000_000 || value > (ulong)(bytes.Length - offset)) throw new InvalidDataException("Lua count exceeds the data or decoder limit.");
            return (int)value;
        }
        private Bytes? String()
        {
            ulong length = Unsigned(stringSize);
            if (length == 0) return null;
            if (length > MaximumBytes || length > (ulong)(bytes.Length - offset)) throw new InvalidDataException("Invalid Lua string length.");
            byte[] data = Take((int)length);
            if (data[^1] != 0) throw new InvalidDataException("Lua string is not null terminated.");
            return new Bytes(data[..^1]);
        }
        public Prototype Read()
        {
            var p = ReadPrototype("<unknown>", 0);
            if (offset != bytes.Length) throw new InvalidDataException("Trailing bytes after Lua chunk; not silently discarded.");
            return p;
        }
        private Prototype ReadPrototype(string parent, int depth)
        {
            if (depth > 48 || ++functions > 10_000) throw new InvalidDataException("Lua prototype depth/count limit exceeded.");
            var source = String(); string name = source == null ? parent : Quote(source);
            Unsigned(4); Unsigned(4); Take(4);
            int count = Count(); instructions += count;
            if (instructions > 1_000_000) throw new InvalidDataException("Lua instruction limit exceeded.");
            var code = new uint[count]; for (int i = 0; i < count; i++) code[i] = (uint)Unsigned(4);
            var constants = new object?[Count()];
            for (int i = 0; i < constants.Length; i++)
            {
                byte tag = Take(1)[0];
                switch (tag)
                {
                    case 0: break;
                    case 1: byte boolean = Take(1)[0]; if (boolean > 1) throw new InvalidDataException("Invalid Lua boolean."); constants[i] = boolean != 0; break;
                    case 3:
                        var n = Take(numberSize); if (little != BitConverter.IsLittleEndian) Array.Reverse(n);
                        constants[i] = numberSize == 8 ? BitConverter.ToDouble(n) : (double)BitConverter.ToSingle(n); break;
                    case 4: constants[i] = String() ?? throw new InvalidDataException("Null string constant."); break;
                    default: throw new InvalidDataException("Unknown Lua constant type " + tag);
                }
            }
            var children = new Prototype[Count()];
            for (int i = 0; i < children.Length; i++) children[i] = ReadPrototype(name, depth + 1);
            int lines = Count(); Take(checked(lines * 4));
            int locals = Count(); for (int i = 0; i < locals; i++) { String(); Unsigned(4); Unsigned(4); }
            int upvalues = Count(); for (int i = 0; i < upvalues; i++) String();
            return new Prototype(name, code, constants, children);
        }
    }

    public static LuaDocument Analyze(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Lua viewer limit is 16 MiB per file.");
        bool compiled = bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 27, 76, 117, 97 });
        if (!compiled)
        {
            string text;
            if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 })) text = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 })) text = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
            else { int skip = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0; text = new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip); }
            if (text.Any(c => c < 32 && c != '\r' && c != '\n' && c != '\t')) throw new InvalidDataException("Binary data is not recognized as Lua source. No text decoding was guessed.");
            return new LuaDocument("Lua source text", "Original source text. Read-only; no code is executed. Encrypted string literals are not automatically decrypted.", text, "-- Source text has no compiled instruction stream.", "-- Source text: use the highlighted source tab.", false);
        }
        var p = new Reader(bytes).Read();
        var disassembly = new StringBuilder(); var constants = new StringBuilder();
        Dump(p, "root", disassembly, constants);
        try
        {
            var table = LiteralTables(p); var source = new StringBuilder("-- Reconstructed Lua 5.1 literal tables; not the original source formatting.\n-- Numeric table keys and binary strings are preserved. No VM execution.\n\n");
            foreach (var entry in table)
            {
                Append(source, entry.Key is Bytes name && IsIdentifier(name) ? Encoding.ASCII.GetString(name.Value) : "_G[" + Quote(entry.Key) + "]");
                Append(source, " = "); Emit(entry.Value, source, new HashSet<Table>(), 0); Append(source, "\n\n");
            }
            return new LuaDocument("Decoded Lua 5.1 tables", "Literal table assignments reconstructed without running Lua. Function/control-flow scripts require a full decompiler. Gateway/ActionRouter encrypted strings remain encrypted.", source.ToString(), disassembly.ToString(), constants.ToString(), true);
        }
        catch (NotSupportedException error)
        {
            return new LuaDocument("Lua 5.1 bytecode", "Not a literal configuration: " + error.Message + " Read the instruction/constants tabs, or choose Decompile with unluac. No approximate source is presented as recovered code.", "-- Full source reconstruction is unavailable in the native literal decoder.\n-- " + error.Message + "\n-- See Bytecode / Constants, or use the optional unluac action.\n", disassembly.ToString(), constants.ToString(), true);
        }
    }
    private static object? Constant(Prototype p, int index) => index >= 0 && index < p.Constants.Length ? p.Constants[index] : throw new InvalidDataException("Constant index outside Lua prototype.");
    private static Table LiteralTables(Prototype p)
    {
        if (p.Children.Length != 0) throw new NotSupportedException("Nested function prototypes are present.");
        var registers = new object?[256]; var globals = new Table();
        object? Rk(int value) => value >= 256 ? Constant(p, value - 256) : registers[value];
        void Set(Table t, object? key, object? value)
        {
            if (key == null || key is Table || key is double n && double.IsNaN(n)) throw new InvalidDataException("Invalid literal table key.");
            if (value == null) t.Remove(key); else t[key] = value;
        }
        for (int pc = 0; pc < p.Code.Length; pc++)
        {
            uint word = p.Code[pc]; int op = (int)(word & 63), a = (int)(word >> 6 & 255), b = (int)(word >> 23 & 511), c = (int)(word >> 14 & 511), bx = (int)(word >> 14);
            switch (op)
            {
                case 0: if (b >= 256) throw new InvalidDataException("Invalid register."); registers[a] = registers[b]; break;
                case 1: registers[a] = Constant(p, bx); break;
                case 2: if (c != 0) throw new NotSupportedException("LOADBOOL has a control-flow skip."); registers[a] = b != 0; break;
                case 3: if (b >= 256) throw new InvalidDataException("Invalid register range."); for (int i = a; i <= b; i++) registers[i] = null; break;
                case 5: if (Constant(p, bx) is not Bytes s || !s.Value.AsSpan().SequenceEqual("_G"u8)) throw new NotSupportedException("Reads a nonliteral global."); registers[a] = globals; break;
                case 7: Set(globals, Constant(p, bx), registers[a]); break;
                case 9: if (registers[a] is not Table target) throw new NotSupportedException("SETTABLE target is not a literal table."); Set(target, Rk(b), Rk(c)); break;
                case 10: registers[a] = new Table(); break;
                case 34:
                    if (b == 0) throw new NotSupportedException("Dynamic SETLIST length.");
                    if (c == 0) { if (++pc >= p.Code.Length) throw new InvalidDataException("Missing SETLIST extra word."); c = checked((int)p.Code[pc]); }
                    if (b + a >= 256 || c < 1) throw new InvalidDataException("Invalid SETLIST range.");
                    if (registers[a] is not Table list) throw new NotSupportedException("SETLIST target is not a literal table.");
                    for (int i = 1; i <= b; i++) Set(list, (double)checked((c - 1) * 50 + i), registers[a + i]); break;
                case 30: if (pc != p.Code.Length - 1 || b != 1) throw new NotSupportedException("Nonfinal or value-returning RETURN."); break;
                default: throw new NotSupportedException("Opcode " + (op < Opcodes.Length ? Opcodes[op] : op.ToString(CultureInfo.InvariantCulture)) + " requires control-flow/function analysis.");
            }
        }
        return globals;
    }
    private static bool IsIdentifier(Bytes b)
    {
        string s = Encoding.ASCII.GetString(b.Value);
        return b.Value.Length > 0 && b.Value.All(x => x < 128) && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_') && !LuaSyntax.Keywords.Contains(s);
    }
    private static string Quote(object? value)
    {
        if (value == null) return "nil";
        if (value is bool b) return b ? "true" : "false";
        if (value is double n) return double.IsFinite(n) ? n.ToString("R", CultureInfo.InvariantCulture) : double.IsNaN(n) ? "(0/0)" : n > 0 ? "(1/0)" : "(-1/0)";
        if (value is not Bytes text) throw new InvalidDataException("Unknown Lua literal.");
        var sb = new StringBuilder("\"");
        foreach (byte c in text.Value)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append((char)c);
            else if (c is >= 32 and <= 126) sb.Append((char)c);
            else sb.Append('\\').Append(c.ToString("D3", CultureInfo.InvariantCulture));
        }
        return sb.Append('"').ToString();
    }
    private static void Append(StringBuilder sb, string text)
    {
        if (sb.Length + text.Length > OutputLimit) throw new NotSupportedException("Readable output exceeds the 8 MiB display/export limit.");
        sb.Append(text);
    }
    private static void Emit(object? value, StringBuilder sb, HashSet<Table> stack, int depth)
    {
        if (value is not Table table) { Append(sb, Quote(value)); return; }
        if (depth > 48 || !stack.Add(table)) throw new NotSupportedException("Cyclic or overly deep literal table.");
        Append(sb, "{\n");
        foreach (var entry in table)
        {
            Append(sb, new string(' ', (depth + 1) * 2));
            Append(sb, entry.Key is Bytes key && IsIdentifier(key) ? Encoding.ASCII.GetString(key.Value) : "[" + Quote(entry.Key) + "]");
            Append(sb, " = "); Emit(entry.Value, sb, stack, depth + 1); Append(sb, ",\n");
        }
        Append(sb, new string(' ', depth * 2) + "}"); stack.Remove(table);
    }
    private static void Dump(Prototype p, string name, StringBuilder instructions, StringBuilder constants)
    {
        if (instructions.Length > OutputLimit - 1024 || constants.Length > OutputLimit - 1024) return;
        instructions.AppendLine($"-- Prototype {name}, source {p.Source}, {p.Code.Length} instructions");
        constants.AppendLine($"-- Prototype {name}");
        for (int i = 0; i < p.Constants.Length; i++)
        {
            string line = $"[{i}] = {Quote(p.Constants[i])}\n";
            if (constants.Length + line.Length > OutputLimit) { constants.AppendLine("-- Constants output truncated."); break; }
            constants.Append(line);
        }
        for (int pc = 0; pc < p.Code.Length; pc++)
        {
            uint w = p.Code[pc]; int op = (int)(w & 63), a = (int)(w >> 6 & 255), b = (int)(w >> 23 & 511), c = (int)(w >> 14 & 511), bx = (int)(w >> 14);
            instructions.AppendLine($"{pc:D6}  {w:X8}  {(op < Opcodes.Length ? Opcodes[op] : "UNKNOWN")}  A={a} B={b} C={c} Bx={bx} sBx={bx - 131071}");
            if (op == 34 && c == 0 && pc + 1 < p.Code.Length) instructions.AppendLine($"{++pc:D6}  {p.Code[pc]:X8}  SETLIST_EXTRA");
            if (instructions.Length > OutputLimit - 256) { instructions.AppendLine("-- Instruction output truncated."); break; }
        }
        for (int i = 0; i < p.Children.Length; i++) Dump(p.Children[i], name + "/" + i, instructions, constants);
    }
}
