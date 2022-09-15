[<AutoOpen>]
module Fun.Build.PipelineContextExtensions

open System
open System.Text
open System.Diagnostics
open Spectre.Console


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
            TimeoutForStep = ValueNone
            TimeoutForStage = ValueNone
            WorkingDir = ValueNone
            Stages = []
            PostStages = []
        }


    /// Return the first stage which its name as specified. It will first search the normal stages, then it will search post stages.
    member this.FindStageByName(name: string) =
        match this.Stages |> List.tryFind (fun x -> x.Name = name) with
        | Some x -> ValueSome x
        | _ ->
            match this.PostStages |> List.tryFind (fun x -> x.Name = name) with
            | Some x -> ValueSome x
            | _ -> ValueNone


    member this.RunStages(stages: StageContext seq, externalCancelToken: Threading.CancellationToken, ?failfast: bool) =
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

            let timeoutForStep = stage.GetTimeoutForStep()
            let timeoutForStage = stage.GetTimeoutForStage()

            use cts = new Threading.CancellationTokenSource(timeoutForStage)
            use linkedCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(cts.Token, externalCancelToken)

            AnsiConsole.Write(Rule())
            AnsiConsole.Write(
                Rule($"STAGE #{i} [bold teal]{stage.Name}[/] started. Stage timeout: {timeoutForStage}ms. Step timeout: {timeoutForStep}ms.")
                    .LeftAligned()
            )
            AnsiConsole.WriteLine()

            let isParallel = stage.IsParallel

            let steps =
                stage.Steps
                |> Seq.map (fun step ->
                    let ts = async {
                        AnsiConsole.MarkupLine $"""[grey]> step started{if isParallel then " in parallel -->" else ":"}[/]"""
                        let! result = step stage
                        AnsiConsole.MarkupLine $"""[gray]> step finished{if isParallel then " in parallel." else "."}[/]"""
                        AnsiConsole.WriteLine()
                        if result <> 0 then
                            failwith $"Step finished without a success exist code. {result}"
                    }
                    Async.StartChild(ts, timeoutForStep)
                )

            try
                let ts =
                    if isParallel then
                        async {
                            let! completers = steps |> Async.Parallel
                            do! Async.Parallel completers |> Async.Ignore
                        }
                    else
                        async {
                            for step in steps do
                                let! completer = step
                                do! completer
                        }
                Async.RunSynchronously(ts, cancellationToken = linkedCTS.Token)
            with ex ->
                AnsiConsole.MarkupLine $"[red]> step failed: {ex.Message}[/]"
                AnsiConsole.WriteException ex
                AnsiConsole.WriteLine()
                hasError <- true

            AnsiConsole.Write(Rule($"""STAGE #{i} [bold {if hasError then "red" else "teal"}]{stage.Name}[/] finished""").LeftAligned())
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

        let timeoutForPipeline = this.Timeout |> ValueOption.map (fun x -> int x.TotalMilliseconds) |> ValueOption.defaultValue -1

        AnsiConsole.MarkupLine $"[bold lime]Run PIPELINE {this.Name}[/]. Total timeout: {timeoutForPipeline}ms."
        AnsiConsole.WriteLine()


        let sw = Stopwatch.StartNew()
        use cts = new Threading.CancellationTokenSource(timeoutForPipeline)

        Console.CancelKeyPress.Add(fun _ ->
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine "Pipeline is cancelled by console."
            AnsiConsole.WriteLine()
            cts.Cancel()
        )

        AnsiConsole.MarkupLine $"[grey]Run stages[/]"
        let hasFailedStage = this.RunStages(this.Stages, cts.Token, failfast = true)
        AnsiConsole.MarkupLine $"[grey]Run stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()

        AnsiConsole.MarkupLine $"[grey]Run post stages[/]"
        let hasFailedPostStage = this.RunStages(this.PostStages, cts.Token, failfast = false)
        AnsiConsole.MarkupLine $"[grey]Run post stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()

        let hasError = hasFailedStage || hasFailedPostStage

        AnsiConsole.MarkupLine $"""[bold {if hasError then "red" else "lime"}]Run PIPELINE {this.Name} finished in {sw.ElapsedMilliseconds} ms[/]"""
        AnsiConsole.WriteLine()

        if hasError then failwith "Pipeline is failed."
