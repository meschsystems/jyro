namespace Mesch.Jyro

open System
open System.Diagnostics

/// Records per-stage timing for the Jyro compilation and execution pipeline.
/// Mutable class - create one instance, pass it through the pipeline, then read results.
[<Sealed>]
type JyroPipelineStats() =
    let mutable parse = TimeSpan.Zero
    let mutable validate = TimeSpan.Zero
    let mutable link = TimeSpan.Zero
    let mutable compile = TimeSpan.Zero
    let mutable execute = TimeSpan.Zero
    let mutable deserialize = TimeSpan.Zero

    /// Time spent in the Parse stage (source -> AST). Zero for .jyrx files.
    member _.Parse with get() = parse and set(v) = parse <- v

    /// Time spent in the Validate stage (AST -> validated AST). Zero for .jyrx files.
    member _.Validate with get() = validate and set(v) = validate <- v

    /// Time spent in the Link stage (validated AST -> linked program). Zero for .jyrx files.
    member _.Link with get() = link and set(v) = link <- v

    /// Time spent in the Compile stage (linked program -> LINQ Expression Tree delegate).
    member _.Compile with get() = compile and set(v) = compile <- v

    /// Time spent in the Execute stage (running the compiled delegate).
    member _.Execute with get() = execute and set(v) = execute <- v

    /// Time spent in the Deserialize stage (.jyrx bytes -> AST). Zero for .jyro files.
    member _.Deserialize with get() = deserialize and set(v) = deserialize <- v

    /// Total elapsed time across all stages.
    member this.Total =
        this.Parse + this.Validate + this.Link + this.Compile + this.Execute + this.Deserialize

    /// Whether the pipeline ran from a .jyrx file (Deserialize > 0, Parse = 0).
    member this.IsFromJyrx = this.Deserialize > TimeSpan.Zero

    /// Helper: time a function and record the elapsed time into the given setter.
    static member TimeStage(setter: TimeSpan -> unit, f: unit -> 'T) : 'T =
        let sw = Stopwatch.StartNew()
        let result = f()
        sw.Stop()
        setter sw.Elapsed
        result
