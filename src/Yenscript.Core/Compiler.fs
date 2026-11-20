namespace Yenscript.Core

/// Embedding facade: returns WAT. The caller supplies package text and invokes WABT.
module Compiler =
    let compile source = WasmCompiler.compile source

    let compileWithPackages packages source =
        WasmCompiler.compileWithPackages packages source

module Core =
    let compile source = Compiler.compile source

    let compileWithPackages packages source =
        Compiler.compileWithPackages packages source
