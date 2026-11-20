namespace Yenscript.Cli.Tests;

internal sealed class TestDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "yenscript-tests-" + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "src");
    public string Output => Path.Combine(Root, "out");

    public TestDirectory() => Directory.CreateDirectory(Source);
    public string Write(string relative, string text)
    {
        var path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
