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
            Mode = Mode.Execution
            CmdArgs = Seq.toList (Environment.GetCommandLineArgs())
            EnvVars = envVars |> Seq.map (fun (KeyValue (k, v)) -> k, v) |> Map.ofSeq
            AcceptableExitCodes = set [| 0 |]
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
        let stageExns = ResizeArray<exn>()

        while i < stages.Length && (not failfast || not hasError) do
            let stage = stages[i]
            let isActive = stage.IsActive stage

            if isActive then
                let isSuccess, exns = stage.Run(StageIndex.Stage i, cancelToken)
                stageExns.AddRange exns
                hasError <- hasError || not isSuccess
            else
                AnsiConsole.Write(Rule())
                AnsiConsole.MarkupLine $"STAGE #{i} [bold turquoise4]{stage.Name}[/] is inactive"
                AnsiConsole.Write(Rule())

            i <- i + 1

        hasError, stageExns


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
        let pipelineExns = ResizeArray<exn>()
        use cts = new Threading.CancellationTokenSource(timeoutForPipeline)

        Console.CancelKeyPress.Add(fun e ->
            cts.Cancel()
            e.Cancel <- true

            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine "[yellow]Pipeline is cancelled by console.[/]"
            AnsiConsole.WriteLine()
        )

        AnsiConsole.MarkupLine $"[turquoise4]Run stages[/]"
        let hasFailedStage, stageExns = this.RunStages(this.Stages, cts.Token, failfast = true)
        pipelineExns.AddRange stageExns
        AnsiConsole.MarkupLine $"[turquoise4]Run stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()

        let mutable hasFailedPostStage = false
        if cts.IsCancellationRequested |> not then
            AnsiConsole.MarkupLine $"[turquoise4]Run post stages[/]"
            let result, postStageExns = this.RunStages(this.PostStages, cts.Token, failfast = false)
            hasFailedPostStage <- result
            pipelineExns.AddRange postStageExns
            AnsiConsole.MarkupLine $"[turquoise4]Run post stages finished[/]"
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

        if pipelineExns.Count > 0 then
            for exn in pipelineExns do
                AnsiConsole.WriteException exn
                AnsiConsole.WriteLine()
            raise (PipelineFailedException("Pipeline is failed because of exception", pipelineExns[0]))
        else if hasError then
            raise (PipelineFailedException "Pipeline is failed because exit code is indicating as successful")


    member pipeline.RunCommandHelp() =
        Console.InputEncoding <- Encoding.UTF8
        Console.OutputEncoding <- Encoding.UTF8

        AnsiConsole.MarkupLine $"Pipeline [green]{pipeline.Name}[/] stages execution options/conditions"
        AnsiConsole.WriteLine()

        let rec run index (stage: StageContext) =
            AnsiConsole.MarkupLine $"[grey]{stage.BuildStepPrefix index}[/]"
            stage.IsActive stage |> ignore
            for inedx, step in List.indexed stage.Steps do
                match step with
                | Step.StepFn _ -> ()
                | Step.StepOfStage s ->
                    run
                        inedx
                        { s with
                            ParentContext = ValueSome(StageParent.Stage stage)
                        }

        pipeline.Stages
        |> List.iteri (fun index stage ->
            run
                index
                { stage with
                    ParentContext = ValueSome(StageParent.Pipeline pipeline)
                }
        )
        pipeline.PostStages
        |> List.iteri (fun index stage ->
            run
                index
                { stage with
                    ParentContext = ValueSome(StageParent.Pipeline pipeline)
                }
        )

        AnsiConsole.MarkupLine ""
        AnsiConsole.MarkupLine "If you are running in commandline you can execute like below:"
        AnsiConsole.MarkupLine "[green]dotnet fsi (your fsharp script).fsx -p (your pipeline name) (some cmd options)[/]"
