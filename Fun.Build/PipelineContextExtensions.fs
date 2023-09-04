﻿namespace rec Fun.Build

open System
open System.Text
open System.Diagnostics
open Fun.Build.Internal
open Spectre.Console
open Fun.Build
open Fun.Build.StageContextExtensionsInternal

module PipelineContextExtensionsInternal =

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
                Description = ValueNone
                Mode = Mode.Execution
                Verify = fun _ -> true
                CmdArgs = Seq.toList (Environment.GetCommandLineArgs())
                EnvVars = envVars |> Seq.map (fun (KeyValue(k, v)) -> k, v) |> Map.ofSeq
                AcceptableExitCodes = set [| 0 |]
                Timeout = ValueNone
                TimeoutForStep = ValueNone
                TimeoutForStage = ValueNone
                WorkingDir = ValueNone
                NoPrefixForStep = true
                NoStdRedirectForStep = false
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


        member this.MakeVerificationStage() =
            { StageContext.Create("") with
                ParentContext = ValueSome(StageParent.Pipeline this)
            }


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
                let isSuccess, exns = stage.Run(StageIndex.Stage i, cancelToken)
                stageExns.AddRange exns
                hasError <- hasError || not isSuccess

                i <- i + 1

            hasError, stageExns


        member this.Run() =
            Console.InputEncoding <- Encoding.UTF8
            Console.OutputEncoding <- Encoding.UTF8

            if String.IsNullOrEmpty this.Name |> not then
                let title = FigletText this.Name
                title.LeftJustified() |> ignore
                title.Color <- Color.Lime
                AnsiConsole.Write title


            if this.Verify(this) |> not then
                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine $"[red]Pipeline verification failed, because some conditions are not met:[/]"
                let pipeline = { this with Mode = Mode.Verification }
                pipeline.Verify pipeline |> ignore
                AnsiConsole.WriteLine()

                raise (PipelineFailedException "Pipeline is failed because verification failed")


            let timeoutForPipeline = this.Timeout |> ValueOption.map (fun x -> int x.TotalMilliseconds) |> ValueOption.defaultValue -1

            AnsiConsole.MarkupLineInterpolated $"Run PIPELINE [bold lime]{this.Name}[/]. Total timeout: {timeoutForPipeline}ms."
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


            AnsiConsole.MarkupLineInterpolated $"""PIPELINE [bold {color}]{this.Name}[/] is {exitText} in {sw.ElapsedMilliseconds} ms"""
            AnsiConsole.WriteLine()


            if cts.IsCancellationRequested then
                raise (PipelineCancelledException "Cancelled by console")

            if pipelineExns.Count > 0 then
                for exn in pipelineExns do
                    AnsiConsole.WriteException exn
                    AnsiConsole.WriteLine()
                raise (PipelineFailedException("Pipeline is failed because of exception", pipelineExns[0]))
            else if hasError then
                AnsiConsole.MarkupLine "[red]Pipeline is failed because result is not indicating as successful[/]"
                raise (PipelineFailedException "Pipeline is failed because result is not indicating as successful")

        member pipeline.RunCommandHelp(verbose: bool) =
            Console.InputEncoding <- Encoding.UTF8
            Console.OutputEncoding <- Encoding.UTF8

            let scriptFile = getFsiFileName ()

            let helpContext = {
                Verbose = verbose
                CmdArgs = Collections.Generic.List()
                EnvArgs = Collections.Generic.List()
            }

            let mode = Mode.CommandHelp helpContext
            let pipeline = { pipeline with Mode = mode }

            AnsiConsole.MarkupLine $"Description:"

            if verbose then
                AnsiConsole.MarkupLineInterpolated $"  Pipeline [green]{pipeline.Name}[/] (stages execution options/conditions)"
            else
                AnsiConsole.MarkupLineInterpolated $"  Pipeline [green]{pipeline.Name}[/] (command only help information)"

            match pipeline.Description with
            | ValueNone -> ()
            | ValueSome x -> AnsiConsole.WriteLine $"  {x}"

            AnsiConsole.WriteLine ""
            AnsiConsole.WriteLine $"Usage:"
            AnsiConsole.WriteLine $"  dotnet fsi {scriptFile} -- -p {pipeline.Name} [options]"
            AnsiConsole.WriteLine $"  dotnet fsi {scriptFile} -- -p {pipeline.Name} -h"
            AnsiConsole.WriteLine $"  dotnet fsi {scriptFile} -- -p {pipeline.Name} -h --verbose"
            AnsiConsole.WriteLine ""

            if verbose then
                AnsiConsole.WriteLine "Options/conditions:"
                AnsiConsole.Console.MarkupLine "> pipeline verification:"
                if pipeline.Verify(pipeline) && verbose then
                    AnsiConsole.Console.MarkupLine "  [grey]no options/conditions[/]"
                AnsiConsole.Console.MarkupLine "> stages activation:"

            let rec run (stage: StageContext) =
                if verbose then
                    AnsiConsole.MarkupLineInterpolated $"  [grey]{stage.GetNamePath()}[/]"

                if stage.IsActive stage && verbose then
                    AnsiConsole.Console.MarkupLine $"{stage.BuildIndent()}[grey]no options/conditions[/]"

                for step in stage.Steps do
                    match step with
                    | Step.StepFn _ -> ()
                    | Step.StepOfStage s ->
                        run
                            { s with
                                ParentContext = ValueSome(StageParent.Stage stage)
                            }

            pipeline.Stages
            |> List.iter (fun stage ->
                run
                    { stage with
                        ParentContext = ValueSome(StageParent.Pipeline pipeline)
                    }
            )

            pipeline.PostStages
            |> List.iter (fun stage ->
                run
                    { stage with
                        ParentContext = ValueSome(StageParent.Pipeline pipeline)
                    }
            )

            if not verbose then
                let prefix = "  "

                AnsiConsole.WriteLine "Options(collected from pipeline and stages):"

                if helpContext.CmdArgs.Count > 0 then
                    helpContext.CmdArgs
                    |> Seq.distinctBy (fun x -> x.Name)
                    |> Seq.iter (fun x ->
                        makeCommandOption prefix (makeCmdNameForPrint mode x) (defaultArg x.Description "" + makeValuesForPrint x.Values)
                        |> AnsiConsole.WriteLine
                    )

                printHelpOptions ()
                printCommandOption
                    prefix
                    "-v, --verbose"
                    "Make the help information verbose (pipeline structure, conditions detail, cmd options and env args etc.)"

                if helpContext.EnvArgs.Count > 0 then
                    AnsiConsole.WriteLine ""
                    AnsiConsole.WriteLine "ENV variables(collected from pipeline and stages):"
                    helpContext.EnvArgs
                    |> Seq.distinctBy (fun x -> x.Name)
                    |> Seq.iter (fun x ->
                        makeCommandOption prefix (makeEnvNameForPrint x) (defaultArg x.Description "" + makeValuesForPrint x.Values)
                        |> AnsiConsole.WriteLine
                    )

            AnsiConsole.WriteLine ""
