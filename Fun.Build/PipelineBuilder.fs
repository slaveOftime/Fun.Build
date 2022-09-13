[<AutoOpen>]
module Fun.Build.PipelineBuilder

open System


type PipelineBuilder(name: string) =

    member inline _.Run(_: unit) = ()
    member _.Run(build: BuildPipeline) = build.Invoke(PipelineContext(Name = name))

    member inline _.Yield(_: unit) = BuildPipeline id

    member inline _.Yield(stage: StageContext) = stage

    member inline _.Delay([<InlineIfLambda>] fn: unit -> unit) = fn ()
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildPipeline) = BuildPipeline(fun ctx -> fn().Invoke ctx)

    member inline _.Delay([<InlineIfLambda>] fn: unit -> StageContext) =
        BuildPipeline(fun ctx ->
            ctx.Stages.Add(fn ())
            ctx
        )


    member inline _.Combine(stage: StageContext, build: BuildPipeline) =
        BuildPipeline(fun ctx ->
            stage.PipelineContext <- ValueSome ctx
            ctx.Stages.Add stage
            build.Invoke ctx
        )

    member inline this.Combine(build: BuildPipeline, stage: StageContext) = this.Combine(stage, build)


    member inline _.For([<InlineIfLambda>] build: BuildPipeline, [<InlineIfLambda>] fn: unit -> BuildPipeline) =
        BuildPipeline(fun ctx -> fn().Invoke(build.Invoke ctx))


    /// Set default timeout for all stages, stage can also set timeout to override this. Unit is seconds.
    [<CustomOperation("timeout")>]
    member inline _.timeout([<InlineIfLambda>] build: BuildPipeline, seconds: int) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            ctx.Timeout <- ValueSome(TimeSpan.FromSeconds seconds)
            ctx
        )

    /// Set default timeout for all stages, stage can also set timeout to override this.
    [<CustomOperation("timeout")>]
    member inline _.timeout([<InlineIfLambda>] build: BuildPipeline, time: TimeSpan) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            ctx.Timeout <- ValueSome time
            ctx
        )

    /// Add or override environment variables
    [<CustomOperation("envArgs")>]
    member inline _.envArgs([<InlineIfLambda>] build: BuildPipeline, kvs: seq<string * string>) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            kvs |> Seq.iter (fun (k, v) -> ctx.EnvVars[ k ] <- v)
            ctx
        )

    /// Reset command line args.
    /// By default, it will use Environment.GetCommandLineArgs()
    [<CustomOperation("cmdArgs")>]
    member inline _.cmdArgs([<InlineIfLambda>] build: BuildPipeline, args: seq<string>) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            ctx.CmdArgs.Clear()
            ctx.CmdArgs.AddRange args
            ctx
        )


    /// Run this pipeline now
    [<CustomOperation("runImmediate")>]
    member _.runImmediate(build: BuildPipeline) = build.Invoke(PipelineContext(Name = name)).Run()


    /// If set to true (default), then will check the if the CmdArgs contains -p if it append an arugment which is equal to the pipeline name, if all checks then it will run the pipeline.
    /// If set to false and if there is no -p in the CmdArgs, then it will also run the pipeline.
    [<CustomOperation("runIfOnlySpecified")>]
    member _.runIfOnlySpecified(build: BuildPipeline, ?specified: bool) =
        let specified = defaultArg specified true
        let ctx = build.Invoke(PipelineContext(Name = name))
        let index = ctx.CmdArgs |> Seq.tryFindIndex ((=) "-p")

        match index with
        | Some index when ctx.CmdArgs.Count > index + 1 -> if ctx.CmdArgs[index + 1] = ctx.Name then ctx.Run()
        | None when not specified -> ctx.Run()
        | _ -> ()


/// Build a pipeline with a specific name.
/// You can compose stage with it, so they can run in sequence.
let pipeline = PipelineBuilder
