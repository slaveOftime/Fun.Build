[<AutoOpen>]
module Fun.Build.PipelineBuilder

open System


type PipelineBuilder(name: string) =

    member inline _.Run(_: unit) = ()
    member _.Run(build: BuildPipeline) = build.Invoke(PipelineContext(Name = name))

    member inline _.Yield(_: unit) = BuildPipeline id

    member inline _.Yield(stage: StageContext) =
        BuildPipeline(fun ctx ->
            stage.PipelineContext <- Some ctx
            ctx.Stages.Add stage
            ctx
        )

    member inline _.Delay([<InlineIfLambda>] fn: unit -> unit) = fn ()
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildPipeline) = BuildPipeline(fun ctx -> fn().Invoke ctx)


    member inline _.Combine([<InlineIfLambda>] build1: BuildPipeline, [<InlineIfLambda>] build2: BuildPipeline) =
        BuildPipeline(fun ctx ->
            let ctx = build1.Invoke ctx
            build2.Invoke ctx
        )

    member inline _.Combine(stage: StageContext, build: BuildPipeline) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            stage.PipelineContext <- Some ctx
            ctx.Stages.Add stage
            ctx
        )

    member inline _.Combine(build: BuildPipeline, stage: StageContext) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            stage.PipelineContext <- Some ctx
            ctx.Stages.Add stage
            ctx
        )

    member inline _.For([<InlineIfLambda>] build: BuildPipeline, [<InlineIfLambda>] fn: unit -> BuildPipeline) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            fn().Invoke(ctx)
        )


    [<CustomOperation("timeout")>]
    member inline _.timeout([<InlineIfLambda>] build: BuildPipeline, seconds: int) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            ctx.Timeout <- TimeSpan.FromSeconds seconds
            ctx
        )

    [<CustomOperation("envArgs")>]
    member inline _.envArgs([<InlineIfLambda>] build: BuildPipeline, kvs: seq<string * string>) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            kvs |> Seq.iter (fun (k, v) -> ctx.EnvVars[ k ] <- v)
            ctx
        )

    [<CustomOperation("cmdArgs")>]
    member inline _.cmdArgs([<InlineIfLambda>] build: BuildPipeline, args: seq<string>) =
        BuildPipeline(fun ctx ->
            let ctx = build.Invoke ctx
            ctx.CmdArgs.AddRange args
            ctx
        )


    [<CustomOperation("runImmediate")>]
    member _.runImmediate(build: BuildPipeline) = build.Invoke(PipelineContext(Name = name)).Run()


    [<CustomOperation("runIfOnlySpecified")>]
    member _.runIfOnlySpecified(build: BuildPipeline, ?specified: bool) =
        let specified = defaultArg specified true
        let args = Environment.GetCommandLineArgs()
        let index = args |> Seq.tryFindIndex ((=) "-p")

        match index with
        | Some index when args.Length > index + 1 ->
            let ctx = build.Invoke(PipelineContext(Name = name))
            if args[index + 1] = ctx.Name then ctx.Run()
        | None when not specified -> build.Invoke(PipelineContext(Name = name)).Run()
        | _ -> ()


let pipeline = PipelineBuilder
