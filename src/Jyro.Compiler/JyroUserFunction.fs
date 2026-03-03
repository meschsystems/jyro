namespace Mesch.Jyro

open System
open System.Collections.Generic

/// A user-defined function declared in a Jyro script.
/// Created as a shell before function bodies are compiled, enabling mutual recursion.
/// The compiled delegate is set via SetCompiled before any script code executes.
type JyroUserFunction(name: string, signature: JyroFunctionSignature) =
    let mutable compiled: Func<IReadOnlyList<JyroValue>, JyroExecutionContext, JyroValue> option = None

    /// Sets the compiled delegate for this function. Called after the function body is compiled.
    member _.SetCompiled(fn: Func<IReadOnlyList<JyroValue>, JyroExecutionContext, JyroValue>) =
        compiled <- Some fn

    interface IJyroFunction with
        member _.Name = name
        member _.Signature = signature
        member _.Execute(args, ctx) =
            match compiled with
            | Some fn -> fn.Invoke(args, ctx)
            | None -> JyroError.raiseRuntime MessageCode.UndefinedFunctionRuntime [| box name |]

module JyroUserFunction =
    /// Convert an AST type hint to a parameter type
    let paramTypeFromJyroType (t: JyroType option) : ParameterType =
        match t with
        | None -> AnyParam
        | Some AnyType -> AnyParam
        | Some NumberType -> NumberParam
        | Some StringType -> StringParam
        | Some BooleanType -> BooleanParam
        | Some ObjectType -> ObjectParam
        | Some ArrayType -> ArrayParam
        | Some NullType -> NullParam

    /// Build a JyroFunctionSignature from a function name and parameter list
    let buildSignature (name: string) (parameters: (string * JyroType option) list) : JyroFunctionSignature =
        let paramDefs =
            parameters |> List.map (fun (pName, typeHint) ->
                Parameter.Required(pName, paramTypeFromJyroType typeHint))
        { Name = name
          Parameters = paramDefs
          ReturnType = AnyParam
          MinArgs = parameters.Length
          MaxArgs = parameters.Length }

    /// Create a JyroUserFunction shell from a FuncDef AST node's name and parameters
    let createShell (name: string) (parameters: (string * JyroType option) list) : JyroUserFunction =
        let signature = buildSignature name parameters
        JyroUserFunction(name, signature)
