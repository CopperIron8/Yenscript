namespace Yenscript.Core

/// Serializes validated WASM IR to WAT; never reads source AST or files.
module WasmEmitter =
    let emit (ir: WasmModule) =
        let rec render instructions =
            instructions
            |> List.map (function
                | SourcePoint p -> "    nop ;; ys:" + string p.Line + ":" + string p.Column
                | I32Const n -> "    i32.const " + n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                | LocalGet index -> "    local.get " + string index
                | LocalSet index -> "    local.set " + string index
                | IntegerOperator op -> "    " + op
                | Drop -> "    drop"
                | HostFunctionCall index -> "    call $h" + string index
                | FunctionCall index -> "    call $f" + string index
                | FunctionReturn -> "    return"
                | Unreachable -> "    unreachable"
                | TableGet -> "    call $table_get"
                | TableSet -> "    call $table_set"
                | LoadI32 offset -> "    call $nonnull\n    i32.load offset=" + string offset
                | NewTable(temp, elements) ->
                    let prefix =
                        "    i32.const "
                        + string (8 + 4 * elements.Length)
                        + "\n    call $alloc\n    local.set "
                        + string temp
                        + "\n    local.get "
                        + string temp
                        + "\n    i32.const "
                        + string elements.Length
                        + "\n    i32.store\n    local.get "
                        + string temp
                        + "\n    i32.const 4\n    i32.store offset=4\n"

                    let stores =
                        elements
                        |> List.mapi (fun index value ->
                            "    local.get "
                            + string temp
                            + "\n"
                            + render value
                            + "\n    i32.store offset="
                            + string (8 + index * 4))
                        |> String.concat "\n"

                    prefix + stores + "\n    local.get " + string temp
                | IfValue(yes, no) -> "    if (result i32)\n" + render yes + "\n    else\n" + render no + "\n    end"
                | IfBlock(yes, no) -> "    if\n" + render yes + "\n    else\n" + render no + "\n    end"
                | WhileBlock(condition, body) ->
                    "    block\n    loop\n"
                    + render condition
                    + "\n    i32.eqz\n    br_if 1\n"
                    + render body
                    + "\n    br 0\n    end\n    end")
            |> String.concat "\n"

        let segments =
            ir.Data
            |> List.map (fun data ->
                let escaped =
                    data.Bytes
                    |> Array.map (fun b -> "\\" + b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
                    |> String.concat ""

                "  (data (i32.const " + string data.Offset + ") \"" + escaped + "\")")

        let functions =
            ir.Functions
            |> List.map (fun f ->
                "  (func $f"
                + string f.Index
                + (List.replicate f.ParameterCount " (param i32)" |> String.concat "")
                + (if f.ReturnsValue then " (result i32)" else "")
                + "\n"
                + (List.replicate f.LocalCount "    (local i32)\n" |> String.concat "")
                + render f.Body
                + "\n  )")
            |> String.concat "\n"

        let quote text =
            "\""
            + (System.Text.Encoding.UTF8.GetBytes(text: string)
               |> Array.map (fun b -> "\\" + b.ToString("x2"))
               |> String.concat "")
            + "\""

        let hostImports =
            ir.Hosts
            |> List.filter (fun h -> h.Module <> "__yenscript")
            |> List.map (fun h ->
                let count = h.ParameterTypes |> List.sumBy (fun t -> if t = "string" then 2 else 1)

                "  (import "
                + quote h.Module
                + " "
                + quote h.Name
                + " (func $host"
                + string h.Index
                + (List.replicate count " (param i32)" |> String.concat "")
                + (if h.ReturnType = "void" then "" else " (result i32)")
                + "))\n")
            |> String.concat ""

        let wrappers =
            ir.Hosts
            |> List.map (fun h ->
                let args =
                    h.ParameterTypes
                    |> List.mapi (fun i t ->
                        if t = "string" && h.Module <> "__yenscript" then
                            "    local.get "
                            + string i
                            + "\n    call $nonnull\n    i32.load\n    local.get "
                            + string i
                            + "\n    i32.load offset=4"
                        else
                            "    local.get " + string i)
                    |> String.concat "\n"

                "  (func $h"
                + string h.Index
                + (List.replicate h.ParameterTypes.Length " (param i32)" |> String.concat "")
                + (if h.ReturnType = "void" then "" else " (result i32)")
                + "\n"
                + args
                + (if h.Module = "__yenscript" then
                       "\n    call $builtin_" + h.Name
                   else
                       "\n    call $host" + string h.Index)
                + ")\n")
            |> String.concat ""

        "(module\n"
        + hostImports
        + "  (memory (export \"memory\") "
        + string ir.MemoryPages
        + ")\n"
        + "  (func $nonnull (param $p i32) (result i32) local.get $p i32.eqz if unreachable end local.get $p)\n"
        + WasmRuntime.allocator (
            max
                1024
                (ir.Data
                 |> List.fold (fun endOffset data -> max endOffset (data.Offset + data.Bytes.Length)) 1024)
            |> fun n -> (n + 7) / 8 * 8
        )
        + WasmBuiltins.functions
        + wrappers
        + functions
        + "\n"
        + "  (func (export \"_start\") call $f"
        + string ir.EntryPoint
        + ")\n"
        + String.concat "\n" segments
        + "\n)\n"
