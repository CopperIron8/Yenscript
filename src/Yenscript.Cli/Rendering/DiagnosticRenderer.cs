using Yenscript.Core;

namespace Yenscript.Cli.Rendering;

public static class DiagnosticRenderer
{
    public static void Render(TextWriter writer, string path, string source, Diagnostic diagnostic)
    {
        var line = Math.Max(1, diagnostic.Position.Line);
        var column = Math.Max(1, diagnostic.Position.Column);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var text = line <= lines.Length ? lines[line - 1] : "";
        var label = diagnostic.Severity == Severity.Warning ? "warning" : "error";
        writer.WriteLine($"{label}[{diagnostic.Code}]: {diagnostic.Message.TrimEnd()}");
        writer.WriteLine($"  --> {path}:{line}:{column}");
        writer.WriteLine("   |");
        writer.WriteLine($"{line,2} | {text}");
        var prefix = text[..Math.Min(column - 1, text.Length)];
        writer.WriteLine($"   | {new string(prefix.Select(c => c == '\t' ? '\t' : ' ').ToArray())}^");
    }
}
