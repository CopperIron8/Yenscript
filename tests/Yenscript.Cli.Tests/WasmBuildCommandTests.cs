using System.Text.Json;
using Xunit;
using Yenscript.Cli.Commands;
using Yenscript.Cli.Io;

namespace Yenscript.Cli.Tests;

public sealed class WasmBuildCommandTests
{
    [Fact]
    public void CheckRequiresMainAndDoesNotWrite()
    {
        using var dir = new TestDirectory();
        var source = dir.Write("main.ys", "fn main() { var x: int = 2 * 3; }");
        Assert.Equal(0, WasmBuildCommand.Check(source, TextWriter.Null));
        Assert.Empty(Directory.GetFiles(dir.Source, "*.wasm"));
        File.WriteAllText(source, "var x: int = 1;");
        var errors = new StringWriter();
        Assert.Equal(1, WasmBuildCommand.Check(source, errors));
        Assert.Contains("E200", errors.ToString());
    }

    [Fact]
    public async Task MissingAssemblerPreservesPreviousOutput()
    {
        using var dir = new TestDirectory();
        var source = dir.Write("main.ys", "fn main() {}");
        var output = Path.Combine(dir.Root, "previous.wasm");
        File.WriteAllText(output, "previous");
        var errors = new StringWriter();
        Assert.Equal(1, await WasmBuildCommand.RunAsync(source, output,
            Path.Combine(dir.Root, "missing-assembler"), errors, CancellationToken.None));
        Assert.Equal("previous", File.ReadAllText(output));
        Assert.Contains("E205", errors.ToString());
        Assert.Empty(Directory.GetFiles(dir.Root, ".yenscript-*"));
    }

    [Fact]
    public void MetadataRejectsNonWasmAssemblerOutput()
    {
        Assert.Throws<IOException>(() => WasmMetadata.Create([1, 2, 3], "", "main.ys", ""));
    }

    [Fact]
    public void MetadataUsesActualBinaryInstructionOffsets()
    {
        // A complete core module with one type, one function, and nop/end code.
        byte[] binary = [0, 97, 115, 109, 1, 0, 0, 0, 1, 4, 1, 96, 0, 0, 3, 2, 1, 0, 10, 5, 1, 3, 0, 1, 11];
        using var metadata = JsonDocument.Parse(WasmMetadata.Create(binary, "nop ;; ys:4:7", "main.ys", "fn main() {}"));
        var mapping = metadata.RootElement.GetProperty("mappings")[0];
        Assert.Equal(23, mapping.GetProperty("offset").GetInt32());
        Assert.Equal(4, mapping.GetProperty("line").GetInt32());
        Assert.Equal(7, mapping.GetProperty("column").GetInt32());
    }

    [Fact]
    public void MetadataRejectsTruncatedImportWithoutUnhandledException()
    {
        byte[] binary = [0, 97, 115, 109, 1, 0, 0, 0, 2, 1, 1];
        Assert.Throws<IOException>(() => WasmMetadata.Create(binary, "", "main.ys", ""));
    }

    [Fact]
    public async Task BuildDoesNotOverwriteSourceWithWrongOutputExtension()
    {
        using var dir = new TestDirectory();
        var source = dir.Write("main.ys", "fn main() {}");
        Assert.Equal(1, await WasmBuildCommand.RunAsync(source, source, "wat2wasm", TextWriter.Null, CancellationToken.None));
        Assert.Equal("fn main() {}", File.ReadAllText(source));
    }

    [Fact]
    public async Task CliRequiresOutputArgument()
    {
        Assert.NotEqual(0, await Program.Main(["build", "main.ys"]));
    }
}
