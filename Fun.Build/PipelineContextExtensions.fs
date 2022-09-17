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


    member this.RunStages(stages: StageContext seq, cancelToken: Threading.CancellationToken, ?failfast: bool) =
        let failfast = defaultArg failfast true

        let stages =
            stages
            |> Seq.map (fun x ->
                { x with
                    ParentContext = ValueSome(StageParent.Pipeline this)
                }
            )
            |> Seq.toList

        let mutable i = 0
        let mutable hasError = false

        while i < stages.Length && (not failfast || not hasError) do
            let stage = stages[i]
            let isActive = stage.IsActive stage

            if isActive then
                hasError <- stage.Run(ValueSome i, cancelToken) <> 0 || hasError
            else
                AnsiConsole.Write(Rule())
                AnsiConsole.MarkupLine "STAGE #{i} [bold grey]{stage.Name}[/] is inactive"
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

        Console.CancelKeyPress.Add(fun e ->
            cts.Cancel()
            e.Cancel <- true
            
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine "[yellow]Pipeline is cancelled by console.[/]"
            AnsiConsole.WriteLine()
        )

        AnsiConsole.MarkupLine $"[grey]Run stages[/]"
        let hasFailedStage = this.RunStages(this.Stages, cts.Token, failfast = true)
        AnsiConsole.MarkupLine $"[grey]Run stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()

        let mutable hasFailedPostStage = false
        if cts.IsCancellationRequested |> not then
            AnsiConsole.MarkupLine $"[grey]Run post stages[/]"
            hasFailedPostStage <- this.RunStages(this.PostStages, cts.Token, failfast = false)
            AnsiConsole.MarkupLine $"[grey]Run post stages finished[/]"
            AnsiConsole.WriteLine()
            AnsiConsole.WriteLine()


        let hasError = hasFailedStage || hasFailedPostStage

        let color =
            if hasError then "red"
            else if cts.IsCancellationRequested then "yellow"
            else "lime"

        let exitText = if cts.IsCancellationRequested then "canncelled" else "finished"


        AnsiConsole.MarkupLine $"""PIPELINE [bold {color}]{this.Name}[/] is {exitText} in {sw.ElapsedMilliseconds} ms"""
        AnsiConsole.WriteLine()


        if cts.IsCancellationRequested then
            raise (PipelineCancelledException "Cancelled by console")

        if hasError then raise (PipelineFailedException "Pipeline is failed")
