[<AutoOpen>]
module Fun.Build.PipelineContextExtensions

open System
open System.Text
open System.Linq
open System.Diagnostics
open Spectre.Console
open CliWrap


type PipelineContext with

    static member Create(name: string) =
        let envVars = System.Collections.Generic.Dictionary<string, string>()

        for key in Environment.GetEnvironmentVariables().Keys do
            let key = string key
            try
                envVars.Add(key, Environment.GetEnvironmentVariable key)
            with _ ->
                envVars.Add(key, "")

        {
            Name = name
            CmdArgs = Seq.toList (Environment.GetCommandLineArgs())
            EnvVars = envVars |> Seq.map (fun (KeyValue (k, v)) -> k, v) |> Map.ofSeq
            Timeout = ValueNone
            WorkingDir = ValueNone
            Stages = []
            PostStages = []
        }


    member this.RunStages(stages: StageContext seq, ?failfast: bool) =
        let failfast = defaultArg failfast true

        let stages =
            stages
            |> Seq.map (fun x -> { x with PipelineContext = ValueSome this })
            |> Seq.filter (fun x -> x.IsActive x)
            |> Seq.toList

        let mutable i = 0
        let mutable hasError = false

        while i < stages.Length && (not failfast || not hasError) do
            let stage = stages[i]

            let timeout =
                match stage.Timeout, this.Timeout with
                | ValueSome t, _
                | _, ValueSome t -> int t.TotalMilliseconds
                | _ -> 0

            AnsiConsole.Write(Rule())
            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] started").LeftAligned())
            AnsiConsole.WriteLine()

            let isParallel = stage.IsParallel

            let steps =
                stage.Steps
                |> Seq.map (fun step -> async {
                    AnsiConsole.MarkupLine $"""[grey]> start step{if isParallel then " in parallel -->" else ":"}[/]"""
                    let! result = step stage
                    AnsiConsole.MarkupLine $"""[gray]> finished run step{if isParallel then " in parallel." else "."}[/]"""
                    AnsiConsole.WriteLine()
                    if result <> 0 then
                        failwith $"Step finished without a success exist code. {result}"
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
                AnsiConsole.MarkupLine $"[red]> Run step failed: {ex.Message}[/]"
                AnsiConsole.WriteException ex
                AnsiConsole.WriteLine()
                hasError <- true

            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] finished").LeftAligned())
            AnsiConsole.Write(Rule())

            i <- i + 1

        hasError


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


        AnsiConsole.MarkupLine $"[grey]Run stages[/]"
        let hasFailedStage = this.RunStages(this.Stages, failfast = true)
        AnsiConsole.MarkupLine $"[grey]Run stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()

        AnsiConsole.MarkupLine $"[grey]Run post stages[/]"
        let hasFailedPostStage = this.RunStages(this.PostStages, failfast = false)
        AnsiConsole.MarkupLine $"[grey]Run post stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()


        AnsiConsole.MarkupLine $"[bold lime]Run PIPELINE {this.Name} finished in {sw.ElapsedMilliseconds} ms[/]"
        AnsiConsole.WriteLine()

        if hasFailedStage || hasFailedPostStage then failwith "Pipeline is failed."
