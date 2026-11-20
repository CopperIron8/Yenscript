using Xunit;
using Yenscript.Cli.Io;

namespace Yenscript.Cli.Tests;

public sealed class WasmPackageLoaderTests
{
    [Fact]
    public void RelativePackagesResolveDependenciesBesidePackage()
    {
        using var dir = new TestDirectory();
        dir.Write("packages/base.ysp", "package base; version \"1\";");
        dir.Write("packages/custom.ysp", "package custom; version \"1\"; requires \"base.ysp\" version \"1\";");
        var source = "using package \"packages/custom.ysp\" as c; fn main() {}";
        var file = dir.Write("main.ys", source);
        var packages = WasmPackageLoader.Load(file, source);
        Assert.True(packages.ContainsKey("packages/custom.ysp"));
        Assert.True(packages.ContainsKey("base.ysp"));
    }

    [Fact]
    public void NamedPackagesResolveBesideSource()
    {
        using var dir = new TestDirectory();
        dir.Write("custom.ysp", "package custom; version \"1\";");
        var source = "using package custom as c; fn main() {}";
        Assert.True(WasmPackageLoader.Load(dir.Write("main.ys", source), source).ContainsKey("custom"));
    }

    [Fact]
    public void AbsolutePathsAreSupported()
    {
        using var dir = new TestDirectory();
        var package = dir.Write("custom.ysp", "package custom; version \"1\";").Replace('\\', '/');
        var source = "using package \"" + package + "\" as c; fn main() {}";
        Assert.True(WasmPackageLoader.Load(dir.Write("main.ys", source), source).ContainsKey(package));
    }

    [Fact]
    public void DependencyCyclesAreRejected()
    {
        using var dir = new TestDirectory();
        dir.Write("a.ysp", "package a; version \"1\"; requires \"b.ysp\" version \"1\";");
        dir.Write("b.ysp", "package b; version \"1\"; requires \"a.ysp\" version \"1\";");
        var source = "using package a; fn main() {}";
        Assert.Throws<IOException>(() => WasmPackageLoader.Load(dir.Write("main.ys", source), source));
    }
}
