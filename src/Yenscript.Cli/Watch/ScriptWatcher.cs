namespace Yenscript.Cli.Watch;

public sealed class ScriptWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly Debouncer debouncer = new(TimeSpan.FromMilliseconds(300));
    private readonly string? file;

    public ScriptWatcher(string source)
    {
        var path = Path.GetFullPath(source);
        if (File.Exists(path))
        {
            file = path;
            path = Path.GetDirectoryName(path)!;
        }
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Source does not exist: {path}");
        watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = file is null,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Changed += (_, e) => OnPath(e.FullPath);
        watcher.Created += (_, e) => OnPath(e.FullPath);
        watcher.Deleted += (_, e) => OnPath(e.FullPath);
        watcher.Renamed += (_, e) => { OnPath(e.OldFullPath); OnPath(e.FullPath); };
        watcher.Error += (_, _) => debouncer.Signal();
        watcher.EnableRaisingEvents = true;
    }

    private void OnPath(string path)
    {
        if (file is not null)
        {
            if (string.Equals(path, file, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                debouncer.Signal();
        }
        else if (string.Equals(Path.GetExtension(path), ".ys", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(Path.GetExtension(path)))
            debouncer.Signal();
    }

    public Task RunAsync(Action rebuild, CancellationToken cancellationToken) => debouncer.RunAsync(rebuild, cancellationToken);
    public void Dispose() => watcher.Dispose();
}
