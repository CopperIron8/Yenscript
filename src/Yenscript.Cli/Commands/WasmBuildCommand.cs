using System.Diagnostics;
using Yenscript.Cli.Rendering;

namespace Yenscript.Cli.Commands;

public static class WasmBuildCommand
{
    public static int Check(string source, TextWriter stderr)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(source), ".ys", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Source file must have the .ys extension");
            var text = File.ReadAllText(source);
            var compiled = Core.WasmCompiler.compileWithPackages(Io.WasmPackageLoader.Load(source, text), text);
            if (!compiled.IsError) return ExitCodes.Success;
            foreach (var diagnostic in compiled.ErrorValue)
                DiagnosticRenderer.Render(stderr, source, text, diagnostic);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            stderr.WriteLine("error[E205]: " + error.Message);
        }
        return ExitCodes.Failure;
    }

    public static async Task<int> RunAsync(string source, string output, string assembler,
        TextWriter stderr, CancellationToken cancellationToken)
    {
        string? temporary = null;
        try
        {
            if (!string.Equals(Path.GetExtension(source), ".ys", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Source file must have the .ys extension");
            if (!string.Equals(Path.GetExtension(output), ".wasm", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output file must have the .wasm extension");
            var text = await File.ReadAllTextAsync(source, cancellationToken);
            var packages = Io.WasmPackageLoader.Load(source, text);
            var compiled = Core.WasmCompiler.compileWithPackages(packages, text);
            if (compiled.IsError)
            {
                foreach (var diagnostic in compiled.ErrorValue)
                    DiagnosticRenderer.Render(stderr, source, text, diagnostic);
                return ExitCodes.Failure;
            }
            var destination = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".yenscript-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(temporary + ".wat", compiled.ResultValue, cancellationToken);
            var start = new ProcessStartInfo(assembler)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add(temporary + ".wat");
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(temporary + ".wasm");
            using var process = Process.Start(start) ?? throw new IOException("Cannot start wat2wasm");
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            var message = await errors;
            await stdout;
            if (process.ExitCode != 0)
            {
                await stderr.WriteLineAsync("error[E205]: wat2wasm failed: " + message);
                return ExitCodes.Failure;
            }
            var bytes = await File.ReadAllBytesAsync(temporary + ".wasm", cancellationToken);
            var metadata = Io.WasmMetadata.Create(bytes, compiled.ResultValue, source, text);
            await File.WriteAllTextAsync(temporary + ".meta.json", metadata, cancellationToken);
            // Assemble and validate metadata before touching any previous output.
            File.Move(temporary + ".wasm", destination, overwrite: true);
            File.Move(temporary + ".wat", Path.ChangeExtension(destination, ".wat"), overwrite: true);
            File.Move(temporary + ".meta.json", Path.ChangeExtension(destination, ".meta.json"), overwrite: true);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("error[E205]: Compilation cancelled");
            return ExitCodes.Failure;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or OverflowException)
        {
            await stderr.WriteLineAsync("error[E205]: " + error.Message);
            return ExitCodes.Failure;
        }
        finally
        {
            if (temporary is not null)
                foreach (var extension in new[] { ".wat", ".wasm", ".meta.json" })
                    if (File.Exists(temporary + extension)) File.Delete(temporary + extension);
        }
    }
}
