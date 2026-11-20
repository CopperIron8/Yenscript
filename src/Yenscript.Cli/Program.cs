using System.CommandLine;
using Yenscript.Cli.Commands;

namespace Yenscript.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var root = new RootCommand("Yenscript - compile typed .ys programs to WebAssembly.");
        var build = new Command("build", "Compile one source file to .wasm, .wat and .meta.json.");
        var source = new Argument<string>("file.ys");
        var output = new Option<string>("-o", "--out") { Required = true };
        var assembler = new Option<string>("--wat2wasm") { DefaultValueFactory = _ => "wat2wasm" };
        build.Arguments.Add(source);
        build.Options.Add(output);
        build.Options.Add(assembler);
        build.SetAction((parse, token) => WasmBuildCommand.RunAsync(parse.GetValue(source)!,
            parse.GetValue(output)!, parse.GetValue(assembler)!, Console.Error, token));
        root.Subcommands.Add(build);
        var check = new Command("check", "Parse, type-check and lower without invoking WABT or writing files.");
        var checkSource = new Argument<string>("file.ys");
        check.Arguments.Add(checkSource);
        check.SetAction(parse => WasmBuildCommand.Check(parse.GetValue(checkSource)!, Console.Error));
        root.Subcommands.Add(check);
        return await root.Parse(args).InvokeAsync();
    }
}
