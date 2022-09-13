namespace rec Fun.Build

open System
open System.Text
open System.Diagnostics
open Spectre.Console


type StageContext(name: string) =
    let envVars = System.Collections.Generic.Dictionary<string, string>()

    member val Name = name

    member val IsActive = fun () -> true with get, set
    member val IsParallel = fun () -> false with get, set

    member val Timeout: TimeSpan voption = ValueNone with get, set
    member val WorkingDir: string voption = ValueNone with get, set

    member val EnvVars = envVars

    member val PipelineContext = ValueOption<PipelineContext>.None with get, set

    member val Steps = System.Collections.Generic.List<Async<int>>()


type PipelineContext() =
    let cmdArgs = System.Collections.Generic.List<string>(Environment.GetCommandLineArgs())
    let envVars = System.Collections.Generic.Dictionary<string, string>()

    do
        for key in Environment.GetEnvironmentVariables().Keys do
            let key = string key
            try
                envVars.Add(key, Environment.GetEnvironmentVariable key)
            with _ ->
                envVars.Add(key, "")


    member val Name = "" with get, set

    member val CmdArgs = cmdArgs
    member val EnvVars = envVars

    member val Timeout: TimeSpan voption = ValueNone with get, set
    member val WorkingDir: string voption = ValueNone with get, set

    member val Stages = System.Collections.Generic.List<StageContext>()


    member this.Run() =
        Console.InputEncoding <- Encoding.UTF8
        Console.OutputEncoding <- Encoding.UTF8

        if String.IsNullOrEmpty this.Name |> not then
            let title = FigletText this.Name
            title.LeftAligned() |> ignore
            title.Color <- Color.Red
            AnsiConsole.Write title

        AnsiConsole.MarkupLine $"[bold lime]Run PIPELINE {this.Name}[/]"
        AnsiConsole.WriteLine()

        let sw = Stopwatch.StartNew()

        let stages = this.Stages |> Seq.filter (fun x -> x.IsActive()) |> Seq.toList
        let mutable i = 0
        let mutable shouldStop = false

        while i < stages.Length && not shouldStop do
            let stage = stages[i]

            let timeout =
                match stage.Timeout, this.Timeout with
                | ValueSome t, _
                | _, ValueSome t -> int t.TotalMilliseconds
                | _ -> 0

            AnsiConsole.Write(Rule())
            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] started").LeftAligned())
            AnsiConsole.WriteLine()

            let isParallel = stage.IsParallel()

            let steps =
                stage.Steps
                |> Seq.map (fun step -> async {
                    AnsiConsole.MarkupLine $"""[grey]start step{if isParallel then " in parallel -->" else ":"}[/]"""
                    let! result = step
                    AnsiConsole.MarkupLine $"""[gray]finished run step{if isParallel then " in parallel." else "."}[/]"""
                    AnsiConsole.WriteLine()
                    if result <> 0 then failwith "Run step failed."
                }
                )

            try
                if isParallel then
                    let steps = steps |> Async.Parallel
                    if timeout > 0 then
                        Async.RunSynchronously(steps, timeout = timeout) |> ignore
                    else
                        Async.RunSynchronously(steps) |> ignore
                else if timeout > 0 then
                    steps |> Seq.iter (fun step -> Async.RunSynchronously(step, timeout = timeout) |> ignore)
                else
                    steps |> Seq.iter (Async.RunSynchronously >> ignore)
            with ex ->
                AnsiConsole.MarkupLine $"[red]Run step failed: {ex.Message}[/]"
                AnsiConsole.WriteLine()
                shouldStop <- true

            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] finished").LeftAligned())
            AnsiConsole.Write(Rule())
            AnsiConsole.WriteLine()
            AnsiConsole.WriteLine()

            i <- i + 1

        AnsiConsole.MarkupLine $"[bold lime]Run PIPELINE {this.Name} finished in {sw.ElapsedMilliseconds} ms[/]"
        AnsiConsole.WriteLine()


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext

type BuildConditions = delegate of ctx: StageContext * conditions: (unit -> bool) list -> (unit -> bool) list

type BuildStageIsActive = delegate of ctx: StageContext -> unit
