namespace Yenscript.Cli.Io;

public sealed record SourceFile(string Path, string RelativePath, string Text);

public static class SourceLoader
{
    public static IReadOnlyList<SourceFile> Load(string source)
    {
        var full = Path.GetFullPath(source);
        if (File.Exists(full))
        {
            if (!string.Equals(Path.GetExtension(full), ".ys", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Source file must have the .ys extension.");
            return [new(full, Path.GetFileName(full), File.ReadAllText(full))];
        }
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Source does not exist: {full}");
        return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".ys", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(path => new SourceFile(path, Path.GetRelativePath(full, path), File.ReadAllText(path)))
            .ToArray();
    }

    public static string DefaultOutput(string source) => File.Exists(source)
        ? Path.GetDirectoryName(Path.GetFullPath(source))!
        : Path.GetFullPath(source);
}
