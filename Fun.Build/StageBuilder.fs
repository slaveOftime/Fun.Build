[<AutoOpen>]
module Fun.Build.StageBuilder

open System
open System.Threading.Tasks
open Spectre.Console


type StageBuilder(name: string) =

    member _.Run(build: BuildStage) = build.Invoke(StageContext.Create name)


    member inline _.Yield(_: unit) = BuildStage id

    member inline _.Yield([<InlineIfLambda>] condition: BuildStageIsActive) = condition
    member inline _.Yield([<InlineIfLambda>] builder: BuildStep) = builder


    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStage) = BuildStage(fun ctx -> fn().Invoke(ctx))

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) = BuildStage(fun ctx -> { ctx with IsActive = fn().Invoke })

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStep) = BuildStage(fun ctx -> { ctx with Steps = ctx.Steps @ [ fn().Invoke ] })


    member inline _.Combine([<InlineIfLambda>] condition: BuildStageIsActive, [<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx -> build.Invoke { ctx with IsActive = condition.Invoke })

    member inline _.Combine([<InlineIfLambda>] builder: BuildStep, [<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx -> build.Invoke { ctx with Steps = ctx.Steps @ [ builder.Invoke ] })


    member inline _.For([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] fn: unit -> BuildStage) =
        BuildStage(fun ctx -> fn().Invoke(build.Invoke(ctx)))

    member inline _.For([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with IsActive = fn().Invoke }
        )

    member inline _.For([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] fn: unit -> BuildStep) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with Steps = ctx.Steps @ [ fn().Invoke ] }
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


    /// Set if the steps in current stage should run in parallel, default value is true.
    [<CustomOperation("paralle")>]
    member inline _.paralle([<InlineIfLambda>] build: BuildStage, ?value: bool) =
        BuildStage(fun ctx -> { build.Invoke ctx with IsParallel = defaultArg value true })

    /// Set workding dir for all steps under the stage.
    [<CustomOperation("workingDir")>]
    member inline _.workingDir([<InlineIfLambda>] build: BuildStage, dir: string) =
        BuildStage(fun ctx -> { build.Invoke ctx with WorkingDir = ValueSome dir })


    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] buildStep: BuildStep) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with Steps = ctx.Steps @ [ buildStep.Invoke ] }
        )

    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] buildStep: StageContext -> BuildStep) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            let builder = buildStep ctx
                            return! builder.Invoke(ctx)
                        }
                    ]
            }
        )

    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] buildStep: StageContext -> Async<BuildStep>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            let! builder = buildStep ctx
                            return! builder.Invoke(ctx)
                        }
                    ]
            }
        )


    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, exe: string, args: string) =
        BuildStage(fun ctx -> build.Invoke(ctx).AddCommandStep(exe + " " + args))

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, command: string) = BuildStage(fun ctx -> build.Invoke(ctx).AddCommandStep(command))

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> string) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            let commandStr = step ctx
                            use outputStream = Console.OpenStandardOutput()
                            let command = ctx.BuildCommand(commandStr, outputStream)
                            AnsiConsole.MarkupLine $"[green]{commandStr}[/]"
                            let! result = command.ExecuteAsync().Task |> Async.AwaitTask
                            return result.ExitCode
                        }
                    ]
            }
        )

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> Async<string>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            let! commandStr = step ctx
                            use outputStream = Console.OpenStandardOutput()
                            let command = ctx.BuildCommand(commandStr, outputStream)
                            AnsiConsole.MarkupLine $"[green]{commandStr}[/]"
                            let! result = command.ExecuteAsync().Task |> Async.AwaitTask
                            return result.ExitCode
                        }
                    ]
            }
        )


    /// Add a step to run a async.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, step: Async<unit>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun _ -> async {
                            do! step
                            return 0
                        }
                    ]
            }
        )

    /// Add a step to run a async with exit code returned.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, step: Async<int>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun _ -> step
                    ]
            }
        )


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> unit) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            step ctx
                            return 0
                        }
                    ]
            }
        )

    /// Add a step to run and return an exist code.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> int) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun _ -> async { return step ctx }
                    ]
            }
        )


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> Async<unit>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            do! step ctx
                            return 0
                        }
                    ]
            }
        )

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> Async<int>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun _ -> async { return! step ctx }
                    ]
            }
        )


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> Task) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            do! step ctx |> Async.AwaitTask
                            return 0
                        }
                    ]
            }
        )

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> Task<unit>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun ctx -> async {
                            do! step ctx |> Async.AwaitTask
                            return 0
                        }
                    ]
            }
        )

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run([<InlineIfLambda>] build: BuildStage, [<InlineIfLambda>] step: StageContext -> Task<int>) =
        BuildStage(fun ctx ->
            let ctx = build.Invoke ctx
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        fun _ -> async { return! step ctx |> Async.AwaitTask }
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
                        fun ctx -> async {
                            printfn "%s" (msg ctx)
                            return 0
                        }
                    ]
            }
        )

    /// Add a step to run.
    [<CustomOperation("echo")>]
    member inline this.echo([<InlineIfLambda>] build: BuildStage, msg: string) = this.echo (build, (fun _ -> msg))


    /// Set if stage is active, or should run.
    [<CustomOperation("when'")>]
    member inline _.when'([<InlineIfLambda>] build: BuildStage, value: bool) =
        BuildStage(fun ctx -> { build.Invoke ctx with IsActive = fun _ -> value })


    /// Set if stage is active, or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, ?envValue: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenEnvArg(envKey, defaultArg envValue "")
            }
        )

    /// Set if stage is active, or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKey: string, ?argValue: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(argKey, defaultArg argValue "")
            }
        )

    /// Set if stage is active, or should run by check the git branch name.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch([<InlineIfLambda>] build: BuildStage, branch: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenBranch branch
            }
        )


/// Build a stage with multiple steps which will run in sequence by default.
let inline stage name = StageBuilder name

/// Create a command with a formattable string which will encode the arguments as * when print to console.
let inline cmd (commandStr: FormattableString) =
    BuildStep(fun ctx -> async {
        use outputStream = Console.OpenStandardOutput()
        let command = ctx.BuildCommand(commandStr.ToString(), outputStream)
        let args: obj[] = Array.create commandStr.ArgumentCount "*"
        let encryptiedStr = String.Format(commandStr.Format, args)
        AnsiConsole.MarkupLine $"[green]{encryptiedStr}[/]"
        let! result = command.ExecuteAsync().Task |> Async.AwaitTask
        return result.ExitCode
    }
    )
