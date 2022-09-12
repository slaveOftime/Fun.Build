namespace rec Fun.Build

open System
open System.Text
open Spectre.Console


type StageContext(name: string) =
    member val Name = name
    member val IsActive = fun () -> true with get, set
    member val IsParallel = fun () -> false with get, set
    member val Timeout = TimeSpan.FromSeconds 0 with get, set

    member val PipelineContext = Option<PipelineContext>.None with get, set

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

    member val Timeout = TimeSpan.FromSeconds 0 with get, set

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


        for i, stage in this.Stages |> Seq.filter (fun x -> x.IsActive()) |> Seq.indexed do
            let timeout =
                if stage.Timeout.TotalMilliseconds > 0. then
                    stage.Timeout.TotalMilliseconds
                else
                    this.Timeout.TotalMilliseconds

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
                    if timeout > 0. then
                        Async.RunSynchronously(steps, timeout = int timeout) |> ignore
                    else
                        Async.RunSynchronously(steps) |> ignore
                else if timeout > 0. then
                    steps |> Seq.iter (fun step -> Async.RunSynchronously(step, timeout = int timeout) |> ignore)
                else
                    steps |> Seq.iter (Async.RunSynchronously >> ignore)
            with ex ->
                AnsiConsole.MarkupLine $"[red]{ex.Message}[/]"
                AnsiConsole.WriteLine()

            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] finished").LeftAligned())
            AnsiConsole.Write(Rule())
            AnsiConsole.WriteLine()
            AnsiConsole.WriteLine()


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext
