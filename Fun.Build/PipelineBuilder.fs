[<AutoOpen>]
module Fun.Build.PipelineBuilder

open System
open Spectre.Console


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


    [<CustomOperation("post")>]
    member inline _.post([<InlineIfLambda>] build: BuildPipeline, stages: StageContext list) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with PostStages = stages }
        )


    /// Run this pipeline now
    [<CustomOperation("runImmediate")>]
    member _.runImmediate(build: BuildPipeline) = build.Invoke(PipelineContext.Create name).Run()


    /// If set to true (default), then will check the if the CmdArgs contains -p if it append an argument which is equal to the pipeline name, if all checks then it will run the pipeline.
    /// If set to false and if there is no -p in the CmdArgs, then it will also run the pipeline.
    [<CustomOperation("runIfOnlySpecified")>]
    member _.runIfOnlySpecified(build: BuildPipeline, ?specified: bool) =
        let specified = defaultArg specified true
        let ctx = build.Invoke(PipelineContext.Create name)

        let args = ctx.CmdArgs @ Array.toList (Environment.GetCommandLineArgs())
        let isHelp = args |> Seq.exists (fun arg -> arg = "-h" || arg = "--help")
        let pipelineIndex = args |> Seq.tryFindIndex (fun arg -> arg = "-p" || arg = "--pipeline")

        runIfOnlySpecifiedPipelines.Add ctx

        if isHelp then
            match pipelineIndex with
            | Some index when List.length args > index + 1 && args[index + 1] = ctx.Name -> { ctx with Mode = Mode.CommandHelp }.RunCommandHelp()
            | _ -> ()
        else
            match pipelineIndex with
            | Some index when List.length args > index + 1 -> if args[index + 1] = ctx.Name then ctx.Run()
            | None when not specified -> ctx.Run()
            | _ -> ()


/// Build a pipeline with a specific name.
/// You can compose stage with it, so they can run in sequence.
let inline pipeline name = PipelineBuilder name


let printPipelineCommandHelpIfNeeded () =
    let args = Environment.GetCommandLineArgs()
    let isSpecifiedPipeline = args |> Seq.exists (fun arg -> arg = "-p" || arg = "--pipeline")
    let isHelp = args |> Seq.exists (fun arg -> arg = "-h" || arg = "--help")

    if isHelp && not isSpecifiedPipeline then

        if runIfOnlySpecifiedPipelines.Count = 1 then
            let newCtx =
                { runIfOnlySpecifiedPipelines[0] with
                    Mode = Mode.CommandHelp
                }
            newCtx.RunCommandHelp()

        else
            AnsiConsole.MarkupLine "Below are the pipelines which will run if only specified:"
            AnsiConsole.WriteLine()
            for pipeline in runIfOnlySpecifiedPipelines do
                AnsiConsole.MarkupLine $"    [green]{pipeline.Name}[/]"
            AnsiConsole.WriteLine()
            AnsiConsole.WriteLine "You can run below command to check more information:"
            AnsiConsole.MarkupLine "[green]dotnet fsi (your fsharp script).fsx -- -p (your pipeline) -h[/]"
