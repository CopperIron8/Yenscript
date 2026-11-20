using System.Text;

namespace Yenscript.Cli.Io;

public static class OutputWriter
{
    public static bool Write(string path, string text, bool force)
    {
        if (!force && File.Exists(path) && File.ReadAllText(path) == text)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return true;
    }
}
