[<AutoOpen>]
module Fun.Build.StageBuilder

open System
open System.Threading
open System.Threading.Tasks
open Fun.Build.Internal
open Fun.Build.StageContextExtensionsInternal
open Fun.Build.BuiltinCmdsInternal

type StageBuilder(name: string) =

    member _.Run(build: BuildStage) = build.Invoke(StageContext.Create name)


    member inline _.Yield(_: unit) = BuildStage id
    member inline _.Yield(_: obj) = BuildStage id
    member inline _.Yield(stage: StageContext) = stage

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStage) = BuildStage(fun ctx -> fn().Invoke(ctx))

    member inline _.Delay([<InlineIfLambda>] fn: unit -> StageContext) =
        BuildStage(fun ctx -> { ctx with Steps = ctx.Steps @ [ Step.StepOfStage(fn ()) ] })

    member inline _.Combine(stage: StageContext, [<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx -> build.Invoke { ctx with Steps = ctx.Steps @ [ Step.StepOfStage stage ] })

    member inline _.For([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] fn: unit -> BuildStage) =
        BuildStage(fun ctx -> fn().Invoke(build.Invoke(ctx)))

    member inline _.For([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] fn: unit -> StageContext) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with Steps = ctx.Steps @ [ Step.StepOfStage(fn ()) ] }
        )


    member inline _.Yield([<InlineIfLambda>] condition: BuildStageIsActive) = condition

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        BuildStage(fun ctx -> { ctx with IsActive = fun ctx -> fn().Invoke(ctx) })

    member inline _.Combine([<InlineIfLambda>] condition: BuildStageIsActive, [<InlineIfLambda>] build: BuildStage) =
        buildStageIsActive build condition.Invoke

    member inline _.For([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        buildStageIsActive build (fn().Invoke)


    member inline _.Yield([<InlineIfLambda>] builder: BuildStep) = builder

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStep) =
        BuildStage(fun ctx ->
            { ctx with
                Steps = ctx.Steps @ [ Step.StepFn(fn().Invoke) ]
            }
        )

    member inline _.Combine([<InlineIfLambda>] builder: BuildStep, [<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx ->
            build.Invoke
                { ctx with
                    Steps = ctx.Steps @ [ Step.StepFn builder.Invoke ]
                }
        )

    member inline _.For([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] fn: unit -> BuildStep) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps = ctx.Steps @ [ Step.StepFn(fn().Invoke) ]
            }
        )


    member inline _.Combine([<InlineIfLambda>] build1: BuildStage, [<InlineIfLambda>] build2: BuildStage) =
        BuildStage(fun ctx -> build2.Invoke(build1.Invoke(ctx)))

    member inline _.For<'T>(items: 'T seq, [<InlineIfLambda>] fn: 'T -> StageContext) =
        BuildStage(fun ctx ->
            let newStages = items |> Seq.map (fn >> Step.StepOfStage) |> Seq.toList
            { ctx with Steps = ctx.Steps @ newStages }
        )

    member inline _.YieldFrom(stages: StageContext seq) =
        BuildStage(fun ctx ->
            let newStages = stages |> Seq.map Step.StepOfStage |> Seq.toList
            { ctx with Steps = ctx.Steps @ newStages }
        )


    /// Add or override environment variables
    [<CustomOperation("envVars")>]
    member inline _.envVars([<InlineIfLambda>] build: BuildStage, kvs: seq<string * string>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                EnvVars = kvs |> Seq.fold (fun state (k, v) -> Map.add k v state) ctx.EnvVars
            }
        )

    /// Override acceptable exit codes
    [<CustomOperation("acceptExitCodes")>]
    member inline _.acceptExitCodes([<InlineIfLambda>] build: BuildStage, codes: seq<int>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with AcceptableExitCodes = Set.ofSeq codes }
        )

    /// If the stage is ignored (inactive) then it will throw exception
    [<CustomOperation("failIfIgnored")>]
    member inline _.failIfIgnored([<InlineIfLambda>] build: BuildStage, flag) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with FailIfIgnored = flag }
        )

    /// If the stage is ignored (inactive) then it will throw exception
    [<CustomOperation("failIfIgnored")>]
    member inline this.failIfIgnored([<InlineIfLambda>] build: BuildStage) = this.failIfIgnored (build, true)


    /// If the stage has no active sub stage when executing it will throw exception
    [<CustomOperation("failIfNoActiveSubStage")>]
    member inline _.failIfNoActiveSubStage([<InlineIfLambda>] build: BuildStage, flag) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with FailIfNoActiveSubStage = flag }
        )

    /// If the stage has no active sub stage when executing it will throw exception
    [<CustomOperation("failIfNoActiveSubStage")>]
    member inline this.failIfNoActiveSubStage([<InlineIfLambda>] build: BuildStage) = this.failIfNoActiveSubStage (build, true)


    /// Continue current stage's rest steps if any of its steps failed.
    /// By default this operation will set ContinueStepsOnFailure to true.
    [<CustomOperation("continueStepsOnFailure")>]
    member inline _.continueStepsOnFailure([<InlineIfLambda>] build: BuildStage, ?flag) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with ContinueStepsOnFailure = defaultArg flag true }
        )

    /// Continue current stage (mark it as success) even if its steps are failed.
    /// By default this operation will set ContinueStageOnFailure to true.
    [<CustomOperation("continueStageOnFailure")>]
    member inline _.continueStageOnFailure([<InlineIfLambda>] build: BuildStage, ?flag) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with ContinueStageOnFailure = defaultArg flag true }
        )

    /// Continue pipeline execution (consider this stage as success) even if any of the stage's steps is failed.
    /// By default this operation will set both ContinueStepsOnFailure and ContinueStageOnFailure to true, otherwise set both to false.
    [<CustomOperation("continueOnStepFailure")>]
    member inline _.continueOnStepFailure([<InlineIfLambda>] build: BuildStage, ?flag) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            let shouldContinue = defaultArg flag true
            { ctx with
                ContinueStepsOnFailure = shouldContinue
                ContinueStageOnFailure = shouldContinue
            }
        )


    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeout")>]
    member inline _.timeout([<InlineIfLambda>] build: BuildStage, seconds: int) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Timeout = ValueSome(TimeSpan.FromSeconds seconds)
            }
        )

    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeout")>]
    member inline _.timeout([<InlineIfLambda>] build: BuildStage, time: TimeSpan) =
        BuildStage(fun ctx -> { build.Invoke ctx with Timeout = ValueSome time })


    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeoutForStep")>]
    member inline _.timeoutForStep([<InlineIfLambda>] build: BuildStage, seconds: int) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                TimeoutForStep = ValueSome(TimeSpan.FromSeconds seconds)
            }
        )

    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeoutForStep")>]
    member inline _.timeoutForStep([<InlineIfLambda>] build: BuildStage, time: TimeSpan) =
        BuildStage(fun ctx -> { build.Invoke ctx with TimeoutForStep = ValueSome time })


    /// Set if the steps in current stage should run in parallel
    [<CustomOperation("paralle")>]
    member inline _.paralle([<InlineIfLambda>] build: BuildStage, value: bool) =
        BuildStage(fun ctx -> { build.Invoke ctx with IsParallel = fun _ -> value })

    /// Set if the steps in current stage should run in parallel
    [<CustomOperation("paralle")>]
    member inline _.paralle([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] condition) =
        BuildStage(fun ctx -> { build.Invoke ctx with IsParallel = condition })

    /// Set the steps in current stage run in parallel
    [<CustomOperation("paralle")>]
    member inline this.paralle([<InlineIfLambda>] build: BuildStage) = this.paralle (build, true)

    /// Set workding dir for all steps under the stage.
    [<CustomOperation("workingDir")>]
    member inline _.workingDir([<InlineIfLambda>] build: BuildStage, dir: string) =
        BuildStage(fun ctx -> { build.Invoke ctx with WorkingDir = ValueSome dir })

    /// Set if step should print prefix when running, default value is true.
    [<CustomOperation("noPrefixForStep")>]
    member inline _.noPrefixForStep([<InlineIfLambda>] build: BuildStage, value: bool) =
        BuildStage(fun ctx -> { build.Invoke ctx with NoPrefixForStep = value })

    /// Set if step should print prefix when running, default value is true.
    [<CustomOperation("noPrefixForStep")>]
    member inline this.noPrefixForStep([<InlineIfLambda>] build: BuildStage) = this.noPrefixForStep (build, true)


    /// Set if step should print external command standard output
    [<CustomOperation("noStdRedirectForStep")>]
    member inline _.noStdRedirectForStep([<InlineIfLambda>] build: BuildStage, value: bool) =
        BuildStage(fun ctx -> { build.Invoke ctx with NoStdRedirectForStep = value })

    /// Do not print external command standard output for step
    [<CustomOperation("noStdRedirectForStep")>]
    member inline this.noStdRedirectForStep([<InlineIfLambda>] build: BuildStage) = this.noStdRedirectForStep (build, true)


    /// The stage will shuffle its steps executing sequence
    [<CustomOperation("shuffleExecuteSequence")>]
    member inline _.shuffleExecuteSequence([<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx -> { build.Invoke ctx with ShuffleExecuteSequence = true })

    /// If true, then the stage will shuffle its steps executing sequence
    [<CustomOperation("shuffleExecuteSequence")>]
    member inline _.shuffleExecuteSequence([<InlineIfLambda>] build: BuildStage, v) =
        BuildStage(fun ctx -> { build.Invoke ctx with ShuffleExecuteSequence = v })


    /// Add a step.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, buildStep: StageContext -> BuildStep) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, i) -> async {
                            let builder = buildStep ctx
                            return! builder.Invoke(ctx, i)
                        })
                    ]
            }
        )


    /// <summary>
    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    /// </summary>
    /// <param name="cancellationToken">This can be used to cancel a command and mark it as success</param>
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, exe: string, args: string, ?cancellationToken: CancellationToken) =
        BuildStage(fun ctx ->
            build
                .Invoke(ctx)
                .AddCommandStep(
                    (fun _ -> async { return exe + " " + args }),
                    cancellationToken = defaultArg cancellationToken CancellationToken.None
                )
        )

    /// <summary>
    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    /// </summary>
    /// <param name="cancellationToken">This can be used to cancel a command and mark it as success</param>
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, command: string, ?cancellationToken: CancellationToken) =
        BuildStage(fun ctx ->
            build
                .Invoke(ctx)
                .AddCommandStep((fun _ -> async { return command }), cancellationToken = defaultArg cancellationToken CancellationToken.None)
        )

    /// <summary>
    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    /// </summary>
    /// <param name="cancellationToken">This can be used to cancel a command and mark it as success</param>
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> string, ?cancellationToken: CancellationToken) =
        BuildStage(fun ctx ->
            build
                .Invoke(ctx)
                .AddCommandStep((fun ctx -> async { return step ctx }), cancellationToken = defaultArg cancellationToken CancellationToken.None)
        )

    /// <summary>
    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    /// </summary>
    /// <param name="cancellationToken">This can be used to cancel a command and mark it as success</param>
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Async<string>, ?cancellationToken: CancellationToken) =
        BuildStage(fun ctx -> build.Invoke(ctx).AddCommandStep(step, cancellationToken = defaultArg cancellationToken CancellationToken.None))


    /// <summary>
    /// Add a step to run command. This will encrypt information which is provided as formattable arguments when print to console.
    /// </summary>
    /// <param name="cancellationToken">This can be used to cancel a command and mark it as success</param>
    [<CustomOperation("runSensitive")>]
    member inline _.runSensitive([<InlineIfLambda>] build: BuildStage, command: FormattableString, ?cancellationToken: CancellationToken) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, step) ->
                            ctx.RunSensitiveCommand(command, step, cancellationToken = defaultArg cancellationToken CancellationToken.None)
                        )
                    ]
            }
        )


    /// Add a step to run a async.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: Async<unit>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun _ -> async {
                            do! step
                            return Ok()
                        })
                    ]
            }
        )

    /// Add a step to run a async with an exit code returned.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: Async<int>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, _) -> async {
                            let! exitCode = step
                            return ctx.MapExitCodeToResult exitCode
                        })
                    ]
            }
        )


    /// Add a step to run.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> unit) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, _) -> async {
                            step ctx
                            return Ok()
                        })
                    ]
            }
        )

    /// Add a step to run and return an exit code.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> int) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps = ctx.Steps @ [ Step.StepFn(fun (ctx, _) -> async { return ctx.MapExitCodeToResult(step ctx) }) ]
            }
        )


    /// Add a step to run.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Async<unit>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, _) -> async {
                            do! step ctx
                            return Ok()
                        })
                    ]
            }
        )

    /// Add a step to run and return an exit code.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Async<int>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, _) -> async {
                            let! exitCode = step ctx
                            return ctx.MapExitCodeToResult exitCode
                        })
                    ]
            }
        )


    /// Add a step to run with a Result<unit, string> to indicate if step is successful.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Async<Result<unit, string>>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps = ctx.Steps @ [ Step.StepFn(fun (ctx, _) -> step ctx) ]
            }
        )

    /// Add a step to run with a Result<unit, string> to indicate if step is successful.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Task<Result<unit, string>>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps = ctx.Steps @ [ Step.StepFn(fun (ctx, _) -> step ctx |> Async.AwaitTask) ]
            }
        )

    /// Add a step to run with a Result<unit, string> to indicate if step is successful.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Result<unit, string>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps = ctx.Steps @ [ Step.StepFn(fun (ctx, _) -> async { return step ctx }) ]
            }
        )


    /// Add a step to run.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Task) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, _) -> async {
                            do! step ctx |> Async.AwaitTask
                            return Ok()
                        })
                    ]
            }
        )

    /// Add a step to run.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Task<unit>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, _) -> async {
                            do! step ctx |> Async.AwaitTask
                            return Ok()
                        })
                    ]
            }
        )

    /// Add a step to run.
    [<CustomOperation("run")>]
    member _.run(build: BuildStage, step: StageContext -> Task<int>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, _) -> async {
                            let! exitCode = step ctx |> Async.AwaitTask
                            return ctx.MapExitCodeToResult exitCode
                        })
                    ]
            }
        )


    /// Add a step to run.
    [<CustomOperation("echo")>]
    member inline _.echo([<InlineIfLambda>] build: BuildStage, msg: StageContext -> string) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, i) -> async {
                            if ctx.GetNoPrefixForStep() then
                                printfn "%s" (msg ctx)
                            else
                                printfn "%s %s" (ctx.BuildStepPrefix i) (msg ctx)
                            return Ok()
                        })
                    ]
            }
        )

    /// Add a step to run.
    [<CustomOperation("echo")>]
    member inline this.echo([<InlineIfLambda>] build: BuildStage, msg: string) = this.echo (build, (fun _ -> msg))


/// Build a stage with multiple steps which will run in sequence by default.
let inline stage name = StageBuilder name

let inline step x = BuildStep x
