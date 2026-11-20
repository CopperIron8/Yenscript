using Microsoft.FSharp.Collections;
using Yenscript.Core;

namespace Yenscript.Cli.Io;

/// File resolution belongs to the host, never to the compiler core.
public static class WasmPackageLoader
{
    public static FSharpMap<string, string> Load(string sourcePath, string source)
    {
        var syntax = Parser.parse(source);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        if (syntax.IsError) return MapModule.OfSeq(sources.Select(p => Tuple.Create(p.Key, p.Value)));
        var active = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var visited = new HashSet<string>(active.Comparer);
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        void Visit(string reference, string directory)
        {
            if (reference == "system") return;
            var filename = Path.HasExtension(reference) ? reference : reference + ".ysp";
            if (!string.Equals(Path.GetExtension(filename), ".ysp", StringComparison.OrdinalIgnoreCase))
                throw new IOException("External packages must use the .ysp extension");
            if (active.Count >= 128 || sources.Count >= 1024)
                throw new IOException("Package dependency graph exceeds resolver limits");
            var path = Path.GetFullPath(Path.Combine(directory, filename));
            if (active.Contains(path)) throw new IOException($"Package dependency cycle at {path}");
            var text = File.ReadAllText(path);
            if (sources.TryGetValue(reference, out var existing) && existing != text)
                throw new IOException($"Conflicting package reference: {reference}");
            sources[reference] = text;
            if (visited.Contains(path)) return;
            var package = WasmPackages.parse(text);
            if (package.IsError) throw new IOException($"Invalid package {path}: {package.ErrorValue.Head.Message}");
            active.Add(path);
            foreach (var dependency in package.ResultValue.Dependencies)
                Visit(dependency.Item1, Path.GetDirectoryName(path)!);
            active.Remove(path);
            visited.Add(path);
        }
        foreach (var member in syntax.ResultValue.Members)
            if (member is TopLevel.UsingPackage package) Visit(package.packageName, baseDirectory);
        return MapModule.OfSeq(sources.Select(p => Tuple.Create(p.Key, p.Value)));
    }
}
