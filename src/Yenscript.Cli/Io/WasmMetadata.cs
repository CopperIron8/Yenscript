using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yenscript.Cli.Io;

/// Reads the assembled binary; metadata offsets are never guessed from WAT lines.
public static class WasmMetadata
{
    public sealed record Mapping(int FunctionIndex, int Offset, int FunctionOffset, int Line, int Column);
    public sealed record Document(int Version, string Source, string SourceSha256, string WasmSha256, string OffsetConvention, List<Mapping> Mappings);

    public static string Create(byte[] binary, string wat, string source, string sourceText)
    {
        try { return CreateChecked(binary, wat, source, sourceText); }
        catch (Exception error) when (error is IndexOutOfRangeException or OverflowException or ArgumentOutOfRangeException)
        {
            throw new IOException("Malformed WebAssembly while reading instruction metadata", error);
        }
    }

    private static string CreateChecked(byte[] binary, string wat, string source, string sourceText)
    {
        if (binary.Length < 8 || !binary.AsSpan(0, 8).SequenceEqual(new byte[] { 0, 97, 115, 109, 1, 0, 0, 0 }))
            throw new IOException("Assembler output is not a core WebAssembly v1 module");
        var points = Regex.Matches(wat, @";; ys:(\d+):(\d+)")
            .Select(m => (Line: int.Parse(m.Groups[1].Value), Column: int.Parse(m.Groups[2].Value))).ToArray();
        var mappings = new List<Mapping>();
        var cursor = 8;
        var importedFunctions = 0;
        var point = 0;
        int Uleb()
        {
            uint value = 0;
            var shift = 0;
            while (true)
            {
                if (cursor >= binary.Length || shift >= 35) throw new IOException("Invalid LEB128");
                var b = binary[cursor++];
                value |= (uint)(b & 127) << shift;
                if ((b & 128) == 0) break;
                shift += 7;
            }
            if (value > int.MaxValue) throw new IOException("WASM size exceeds supported metadata limits");
            return (int)value;
        }
        void SkipLeb()
        {
            for (var i = 0; i < 5; i++)
            {
                if (cursor >= binary.Length) throw new IOException("Truncated LEB128");
                if ((binary[cursor++] & 128) == 0) return;
            }
            throw new IOException("Invalid i32 LEB128");
        }
        void SkipName()
        {
            var length = Uleb();
            cursor = checked(cursor + length);
            if (cursor > binary.Length) throw new IOException("Truncated WASM name");
        }
        while (cursor < binary.Length)
        {
            var section = binary[cursor++];
            var length = Uleb();
            var end = checked(cursor + length);
            if (end > binary.Length) throw new IOException("Truncated WASM section");
            if (section == 2)
            {
                var count = Uleb();
                for (var i = 0; i < count; i++)
                {
                    SkipName(); SkipName();
                    if (binary[cursor++] != 0) throw new IOException("Only function imports are supported");
                    Uleb(); importedFunctions++;
                }
            }
            if (section == 10)
            {
                var count = Uleb();
                for (var f = 0; f < count; f++)
                {
                    var size = Uleb();
                    var bodyStart = cursor;
                    var bodyEnd = checked(cursor + size);
                    if (bodyEnd > end) throw new IOException("Truncated function body");
                    var locals = Uleb();
                    for (var i = 0; i < locals; i++) { Uleb(); cursor++; }
                    (int Line, int Column)? active = null;
                    while (cursor < bodyEnd)
                    {
                        var offset = cursor;
                        var opcode = binary[cursor++];
                        if (opcode == 0x01)
                        {
                            if (point >= points.Length) throw new IOException("Unexpected source marker in WASM");
                            active = points[point++];
                        }
                        if (active is { } position)
                            mappings.Add(new(f + importedFunctions, offset, offset - bodyStart, position.Line, position.Column));
                        switch (opcode)
                        {
                            case 0x02: case 0x03: case 0x04: SkipLeb(); break;
                            case 0x0c:
                            case 0x0d:
                            case 0x10:
                            case 0x20:
                            case 0x21:
                            case 0x22:
                            case 0x23:
                            case 0x24: Uleb(); break;
                            case 0x28: case 0x2d: case 0x36: case 0x3a: Uleb(); Uleb(); break;
                            case 0xfc:
                                if (Uleb() != 10) throw new IOException("Unsupported bulk-memory instruction");
                                Uleb(); Uleb(); break;
                            case 0x3f: case 0x40: Uleb(); break;
                            case 0x41: SkipLeb(); break;
                            case 0x00: case 0x01: case 0x05: case 0x0b: case 0x0f: case 0x1a: break;
                            default:
                                if (opcode is < 0x45 or > 0x78)
                                    throw new IOException($"Unsupported instruction 0x{opcode:x2} in metadata reader");
                                break;
                        }
                    }
                    if (cursor != bodyEnd) throw new IOException("Invalid function instruction boundary");
                }
            }
            cursor = end;
        }
        if (point != points.Length) throw new IOException("WASM source markers do not match WAT");
        var document = new Document(1, Path.GetFullPath(source),
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sourceText))).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant(),
            "absolute-byte-offset-and-function-body-relative-offset", mappings);
        return JsonSerializer.Serialize(document, MetadataJsonContext.Default.Document);
    }
}
