namespace Mesch.Jyro

open System
open System.Collections.Generic
open System.Threading
open Mesch.Jyro.Linker

/// Fluent builder for Jyro scripts
type JyroBuilder() =
    let mutable source: string option = None
    let mutable compiledBytes: byte[] option = None
    let mutable data: JyroValue = JyroNull.Instance
    let functions = ResizeArray<IJyroFunction>()
    let mutable includeStdlib = false
    let mutable executionOptions: JyroExecutionOptions option = None
    let mutable cancellationToken: CancellationToken option = None
    let mutable stats: JyroPipelineStats option = None

    /// Set the script source code
    member this.WithSource(script: string) =
        source <- Some script
        compiledBytes <- None
        this

    /// Set precompiled .jyrx binary data (skips parsing and validation)
    member this.WithCompiledBytes(bytes: byte[]) =
        compiledBytes <- Some bytes
        source <- None
        this

    /// Set the input data
    member this.WithData(inputData: JyroValue) =
        data <- inputData
        this

    /// Set the input data from a .NET object
    member this.WithData(inputData: obj) =
        data <- JyroValue.FromObject(inputData)
        this

    /// Set the input data from JSON
    member this.WithJson(json: string) =
        data <- JyroValue.FromJson(json)
        this

    /// Add a custom function
    member this.AddFunction(func: IJyroFunction) =
        functions.Add(func)
        this

    /// Add multiple custom functions
    member this.AddFunctions(funcs: IJyroFunction seq) =
        functions.AddRange(funcs)
        this

    /// Include standard library functions (excluded by default)
    member this.UseStdlib() =
        includeStdlib <- true
        this

    /// Set execution options (resource limits)
    member this.WithExecutionOptions(options: JyroExecutionOptions) =
        executionOptions <- Some options
        this

    /// Set maximum execution time
    member this.WithMaxExecutionTime(time: TimeSpan) =
        let opts = defaultArg executionOptions JyroExecutionOptions.Default
        executionOptions <- Some { opts with MaxExecutionTime = time }
        this

    /// Set maximum statement count
    member this.WithMaxStatements(count: int) =
        let opts = defaultArg executionOptions JyroExecutionOptions.Default
        executionOptions <- Some { opts with MaxStatements = count }
        this

    /// Set maximum loop iterations
    member this.WithMaxLoopIterations(count: int) =
        let opts = defaultArg executionOptions JyroExecutionOptions.Default
        executionOptions <- Some { opts with MaxLoopIterations = count }
        this

    /// Set maximum call depth
    member this.WithMaxCallDepth(depth: int) =
        let opts = defaultArg executionOptions JyroExecutionOptions.Default
        executionOptions <- Some { opts with MaxCallDepth = depth }
        this

    /// Set a cancellation token for cooperative cancellation of script execution.
    /// When combined with execution options, the token is linked with the limiter's
    /// time-based cancellation. Custom functions can observe ctx.CancellationToken
    /// to cancel blocking operations like HTTP requests.
    member this.WithCancellationToken(token: CancellationToken) =
        cancellationToken <- Some token
        this

    /// Set a pipeline stats collector to record per-stage timing
    member this.WithStats(pipelineStats: JyroPipelineStats) =
        stats <- Some pipelineStats
        this

    /// Get all registered functions
    member private _.GetAllFunctions() : IJyroFunction list =
        let stdlib = if includeStdlib then StdlibRegistry.getAll () else []
        let custom = functions |> Seq.toList
        List.concat [ stdlib; custom ]

    /// Create the execution context with limiter if options are configured
    member private _.CreateExecutionContext() : JyroHostContext =
        let ct = cancellationToken
        match executionOptions with
        | Some opts ->
            let limiter = JyroResourceLimiter(opts, ?externalToken = ct)
            JyroHostContext(limiter)
        | None ->
            JyroHostContext(?cancellationToken = ct)

    /// Compile the script from source or precompiled .jyrx bytes
    member this.Compile() : JyroResult<CompiledProgram> =
        let allFunctions = this.GetAllFunctions()
        match stats with
        | Some s ->
            match compiledBytes, source with
            | Some bytes, _ -> Compiler.compileFromJyrxTimed bytes allFunctions s
            | _, Some src -> Compiler.compileFromSourceTimed src allFunctions s
            | None, None ->
                JyroResult<CompiledProgram>.Failure(
                    DiagnosticMessage.Error(MessageCode.UnknownParserError, "No source code or compiled bytes provided"))
        | None ->
            match compiledBytes, source with
            | Some bytes, _ -> Compiler.compileFromJyrx bytes allFunctions
            | _, Some src -> Compiler.compileFromSource src allFunctions
            | None, None ->
                JyroResult<CompiledProgram>.Failure(
                    DiagnosticMessage.Error(MessageCode.UnknownParserError, "No source code or compiled bytes provided"))

    /// Compile the script source to .jyrx binary format
    member this.CompileToBytes() : JyroResult<byte[]> =
        match source with
        | None ->
            JyroResult<byte[]>.Failure(
                DiagnosticMessage.Error(MessageCode.UnknownParserError, "No source code provided"))
        | Some src ->
            let allFunctions = this.GetAllFunctions()
            Compiler.compileToJyrx src allFunctions

    /// Execute the script with the configured data
    member this.Execute() : JyroResult<JyroValue> =
        match this.Compile() with
        | { IsSuccess = false; Messages = msgs } ->
            JyroResult<JyroValue>.Failure(msgs)
        | { Value = Some compiled } ->
            let ctx = this.CreateExecutionContext()
            match stats with
            | Some s -> Compiler.executeTimed compiled data ctx s
            | None -> Compiler.execute compiled data ctx
        | _ ->
            JyroResult<JyroValue>.Failure(
                DiagnosticMessage.Error(MessageCode.UnknownExecutorError, "Compilation returned no program"))

    /// Execute the script and return the result or throw on failure
    member this.ExecuteOrThrow() : JyroValue =
        match this.Execute() with
        | { IsSuccess = true; Value = Some result } -> result
        | { Messages = msgs } ->
            let errorMsg = msgs |> Seq.map (fun m -> m.Message) |> String.concat "; "
            raise (InvalidOperationException(errorMsg))

/// Static factory methods for JyroBuilder
[<AutoOpen>]
module JyroBuilderFactory =
    /// Create a new JyroBuilder
    let jyro () = JyroBuilder()

    /// Quick execute a script with data
    let executeScript (source: string) (data: obj) : JyroResult<JyroValue> =
        JyroBuilder()
            .UseStdlib()
            .WithSource(source)
            .WithData(data)
            .Execute()

    /// Quick execute a script with JSON data
    let executeScriptWithJson (source: string) (json: string) : JyroResult<JyroValue> =
        JyroBuilder()
            .UseStdlib()
            .WithSource(source)
            .WithJson(json)
            .Execute()

/// Extension methods for C# compatibility
[<System.Runtime.CompilerServices.Extension>]
type JyroExtensions =
    /// Execute a Jyro script on this value
    [<System.Runtime.CompilerServices.Extension>]
    static member ExecuteJyro(data: JyroValue, source: string) : JyroResult<JyroValue> =
        JyroBuilder()
            .UseStdlib()
            .WithSource(source)
            .WithData(data)
            .Execute()

    /// Execute a Jyro script on this object
    [<System.Runtime.CompilerServices.Extension>]
    static member ExecuteJyro(data: obj, source: string) : JyroResult<JyroValue> =
        JyroBuilder()
            .UseStdlib()
            .WithSource(source)
            .WithData(data)
            .Execute()
