[<AutoOpen>]
module Fun.Build.PipelineBuilder

open System
open Spectre.Console
open Fun.Build.Internal
open Fun.Build.PipelineContextExtensionsInternal


/// Used to keep registered data for later usage
let private runIfOnlySpecifiedPipelines = System.Collections.Generic.List<PipelineContext>()


type PipelineBuilder(name: string) =

    member inline _.Run(_: unit) = ()

    member _.Run(build: BuildPipeline) =
        let ctx = build.Invoke(PipelineContext.Create name)
        { ctx with
            Stages =
                ctx.Stages
                |> List.map (fun x ->
                    { x with
                        ParentContext = ValueSome(StageParent.Pipeline ctx)
                    }
                )
            PostStages =
                ctx.PostStages
                |> List.map (fun x ->
                    { x with
                        ParentContext = ValueSome(StageParent.Pipeline ctx)
                    }
                )
        }


    member inline _.Yield(_: unit) = BuildPipeline id

    member inline _.Yield(stage: StageContext) = stage


    member inline _.Delay([<InlineIfLambda>] fn: unit -> unit) = fn ()

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildPipeline) = BuildPipeline(fun ctx -> fn().Invoke ctx)

    member inline _.Delay([<InlineIfLambda>] fn: unit -> StageContext) = BuildPipeline(fun ctx -> { ctx with Stages = ctx.Stages @ [ fn () ] })


    member inline _.Combine(stage: StageContext, [<InlineIfLambda>] build: BuildPipeline) =
        BuildPipeline(fun ctx -> build.Invoke { ctx with Stages = ctx.Stages @ [ stage ] })


    member inline _.For([<InlineIfLambda>] build: BuildPipeline, [<InlineIfLambda>] fn: unit -> BuildPipeline) =
        BuildPipeline(fun ctx -> fn().Invoke(build.Invoke ctx))

    member inline _.For([<InlineIfLambda>] build: BuildPipeline, [<InlineIfLambda>] fn: unit -> StageContext) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with Stages = ctx.Stages @ [ fn () ] }
        )


    member inline _.Yield([<InlineIfLambda>] condition: BuildStageIsActive) = condition

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        BuildPipeline(fun ctx ->
            { ctx with
                Verify = fun ctx -> fn().Invoke(ctx.MakeVerificationStage())
            }
        )

    member inline _.Combine([<InlineIfLambda>] condition: BuildStageIsActive, [<InlineIfLambda>] build: BuildPipeline) =
        BuildPipeline(fun ctx ->
            build.Invoke
                { ctx with
                    Verify = fun ctx -> condition.Invoke(ctx.MakeVerificationStage())
                }
        )

    member inline _.For([<InlineIfLambda>] build: BuildPipeline, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Verify = fun ctx -> fn().Invoke(ctx.MakeVerificationStage())
            }
        )


    /// This description is mainly used for command help
    [<CustomOperation("description")>]
    member inline _.description([<InlineIfLambda>] build: BuildPipeline, x) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with Description = ValueSome x }
        )

    /// Verify before run pipeline, will throw PipelineFailedException if return false
    [<CustomOperation("verify")>]
    member inline _.verify([<InlineIfLambda>] build: BuildPipeline, verify) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with Verify = verify }
        )

    /// Set default timeout for all stages, stage can also set timeout to override this. Unit is seconds.
    [<CustomOperation("timeout")>]
    member inline _.timeout([<InlineIfLambda>] build: BuildPipeline, seconds: int) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Timeout = ValueSome(TimeSpan.FromSeconds seconds)
            }
        )

    /// Set default timeout for all stages, stage can also set timeout to override this.
    [<CustomOperation("timeout")>]
    member inline _.timeout([<InlineIfLambda>] build: BuildPipeline, time: TimeSpan) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with Timeout = ValueSome time }
        )


    /// Set default timeout for all stages, stage can also set timeout to override this. Unit is seconds.
    [<CustomOperation("timeoutForStage")>]
    member inline _.timeoutForStage([<InlineIfLambda>] build: BuildPipeline, seconds: int) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                TimeoutForStage = ValueSome(TimeSpan.FromSeconds seconds)
            }
        )

    /// Set default timeout for all stages, stage can also set timeout to override this.
    [<CustomOperation("timeoutForStage")>]
    member inline _.timeoutForStage([<InlineIfLambda>] build: BuildPipeline, time: TimeSpan) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with TimeoutForStage = ValueSome time }
        )


    /// Set default timeout for all stages, stage can also set timeout to override this. Unit is seconds.
    [<CustomOperation("timeoutForStep")>]
    member inline _.timeoutForStep([<InlineIfLambda>] build: BuildPipeline, seconds: int) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                TimeoutForStep = ValueSome(TimeSpan.FromSeconds seconds)
            }
        )

    /// Set default timeout for all stages, stage can also set timeout to override this.
    [<CustomOperation("timeoutForStep")>]
    member inline _.timeoutForStep([<InlineIfLambda>] build: BuildPipeline, time: TimeSpan) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with TimeoutForStep = ValueSome time }
        )


    /// Add or override environment variables
    [<CustomOperation("envVars")>]
    member inline _.envVars([<InlineIfLambda>] build: BuildPipeline, kvs: seq<string * string>) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                EnvVars = kvs |> Seq.fold (fun state (k, v) -> Map.add k v state) ctx.EnvVars
            }
        )

    /// Override acceptable exit codes
    [<CustomOperation("acceptExitCodes")>]
    member inline _.acceptExitCodes([<InlineIfLambda>] build: BuildPipeline, codes: seq<int>) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with AcceptableExitCodes = Set.ofSeq codes }
        )

    /// Reset command line args.
    /// By default, it will use Environment.GetCommandLineArgs()
    [<CustomOperation("cmdArgs")>]
    member inline _.cmdArgs([<InlineIfLambda>] build: BuildPipeline, args: string list) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with CmdArgs = args }
        )

    /// Set working dir for all steps under the stage.
    [<CustomOperation("workingDir")>]
    member inline _.workingDir([<InlineIfLambda>] build: BuildPipeline, dir: string) =
        BuildPipeline(fun ctx -> { build.Invoke ctx with WorkingDir = ValueSome dir })

    /// Set if step should print prefix when running, default value is true.
    [<CustomOperation("noPrefixForStep")>]
    member inline _.noPrefixForStep([<InlineIfLambda>] build: BuildPipeline, value: bool) =
        BuildPipeline(fun ctx -> { build.Invoke ctx with NoPrefixForStep = value })

    [<CustomOperation("noPrefixForStep")>]
    member inline this.noPrefixForStep([<InlineIfLambda>] build: BuildPipeline) = this.noPrefixForStep (build, true)


    [<CustomOperation("post")>]
    member inline _.post([<InlineIfLambda>] build: BuildPipeline, stages: StageContext list) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with PostStages = stages }
        )


    /// Run this pipeline now
    [<CustomOperation("runImmediate")>]
    member _.runImmediate(build: BuildPipeline) = build.Invoke(PipelineContext.Create name).Run()


    /// <summary>
    /// If set to true, the pipeline will be run only if it is specified using <c>-p &lt;name&gt;</c> or <c>--pipeline &lt;name&gt;</c> in the CLI where <c>&lt;name&gt;</c> is the name of the pipeline.
    ///
    /// If set to false, the pipeline will be run if no <c>-p</c> or <c>--pipeline</c> is specified in command line args.
    /// </summary>
    [<CustomOperation("runIfOnlySpecified")>]
    member _.runIfOnlySpecified(build: BuildPipeline, ?specified: bool) =
        let specified = defaultArg specified true
        let ctx = build.Invoke(PipelineContext.Create name)

        let args = ctx.CmdArgs @ Array.toList (Environment.GetCommandLineArgs())
        let isHelp = args |> Seq.exists (fun arg -> arg = "-h" || arg = "--help")
        let verbose = args |> Seq.exists (fun arg -> arg = "-v" || arg = "--verbose")
        let pipelineIndex = args |> Seq.tryFindIndex (fun arg -> arg = "-p" || arg = "--pipeline")

        runIfOnlySpecifiedPipelines.Add ctx

        try
            if isHelp then
                match pipelineIndex with
                | Some index when List.length args > index + 1 && args[index + 1] = ctx.Name -> ctx.RunCommandHelp(verbose)
                | _ -> ()
            else
                match pipelineIndex with
                | Some index when List.length args > index + 1 -> if args[index + 1] = ctx.Name then ctx.Run()
                | None when not specified -> ctx.Run()
                | _ -> ()
        with
        | :? PipelineFailedException
        | :? PipelineCancelledException ->
            // Because this operation is mainly used for command only, as we already printed error messages, so there is no need to throw this exception to cause console to print duplicate message.
            Environment.Exit(1)


/// Build a pipeline with a specific name.
/// You can compose stage with it, so they can run in sequence.
let inline pipeline name = PipelineBuilder name


/// Only when you have -h or --help in your command, it will try to print the help informations.
/// When you use dotnet fsi, please remember to add --, so the help options can be passed in. eg: dotnet fsi demo.fsx -- -h
/// If you only have one specified pipeline, it will try to print its command only help information.
let tryPrintPipelineCommandHelp () =
    let args = Environment.GetCommandLineArgs()
    let isSpecifiedPipeline = args |> Seq.exists (fun arg -> arg = "-p" || arg = "--pipeline")
    let isHelp = args |> Seq.exists (fun arg -> arg = "-h" || arg = "--help")

    let scriptFile = getFsiFileName ()

    if isHelp && not isSpecifiedPipeline then
        if runIfOnlySpecifiedPipelines.Count = 1 then
            let verbose = args |> Seq.exists (fun arg -> arg = "-v" || arg = "--verbose")
            runIfOnlySpecifiedPipelines[0].RunCommandHelp(verbose)

        else
            AnsiConsole.WriteLine "Descriptions:"
            AnsiConsole.WriteLine "  Below are the pipelines which are set as runIfOnlySpecified"
            AnsiConsole.WriteLine ""

            AnsiConsole.WriteLine "Pipelines:"

            if runIfOnlySpecifiedPipelines.Count = 0 then
                AnsiConsole.MarkupLine
                    "[red]* No run if only specified pipelines are found. Please use [green]runIfOnlySpecified[/] at the end of your pipeline CE.[/]"

            for pipeline in runIfOnlySpecifiedPipelines do
                printCommandOption "  " pipeline.Name (defaultValueArg pipeline.Description "")

            AnsiConsole.WriteLine ""
            AnsiConsole.WriteLine "Usage:"
            AnsiConsole.WriteLine $"  dotnet fsi {scriptFile} -- -h"
            AnsiConsole.WriteLine $"  dotnet fsi {scriptFile} -- -p your_pipeline [options]"
            AnsiConsole.WriteLine ""

            AnsiConsole.WriteLine "Options:"
            printHelpOptions ()
            AnsiConsole.WriteLine ""
