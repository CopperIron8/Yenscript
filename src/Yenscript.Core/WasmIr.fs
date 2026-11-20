namespace Yenscript.Core

type WasmInstruction =
    | SourcePoint of Position
    | I32Const of int
    | LocalGet of int
    | LocalSet of int
    | IntegerOperator of string
    | Drop
    | FunctionCall of int
    | HostFunctionCall of int
    | FunctionReturn
    | Unreachable
    | NewTable of temporaryLocal: int * elements: WasmInstruction list list
    | LoadI32 of byteOffset: int
    | TableGet
    | TableSet
    | IfBlock of WasmInstruction list * WasmInstruction list
    | IfValue of WasmInstruction list * WasmInstruction list
    | WhileBlock of condition: WasmInstruction list * body: WasmInstruction list

type WasmData = { Offset: int; Bytes: byte array }

type WasmFunction =
    { Index: int
      ParameterCount: int
      ReturnsValue: bool
      Body: WasmInstruction list
      LocalCount: int }

type WasmHostImport =
    { Index: int
      Module: string
      Name: string
      ParameterTypes: string list
      ReturnType: string }

type WasmModule =
    { Hosts: WasmHostImport list
      Functions: WasmFunction list
      EntryPoint: int
      Data: WasmData list
      MemoryPages: int }
