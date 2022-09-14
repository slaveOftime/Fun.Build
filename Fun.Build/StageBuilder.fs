[<AutoOpen>]
module Fun.Build.StageBuilder

open System
open System.Threading.Tasks
open Spectre.Console


type StageBuilder(name: string) =

    member _.Run(ctx: StageContext) = ctx


    member _.Yield(_: unit) = StageContext name

    member inline _.Yield([<InlineIfLambda>] condition: BuildStageIsActive) = condition
    member inline _.Yield([<InlineIfLambda>] builder: BuildStep) = builder


    member inline _.Delay(fn: unit -> StageContext) = fn ()

    member _.Delay(fn: unit -> BuildStageIsActive) =
        let ctx = StageContext name
        let condition = fn ()
        ctx.IsActive <- condition.Invoke ctx
        ctx

    member _.Delay(fn: unit -> BuildStep) =
        let ctx = StageContext name
        ctx.Steps.Add(fn().Invoke(ctx))
        ctx


    member inline _.Combine(ctx: StageContext, [<InlineIfLambda>] condition: BuildStageIsActive) =
        ctx.IsActive <- condition.Invoke ctx
        ctx

    member inline this.Combine([<InlineIfLambda>] condition: BuildStageIsActive, ctx: StageContext) = this.Combine(ctx, condition)


    member inline _.Combine(ctx: StageContext, [<InlineIfLambda>] builder: BuildStep) =
        ctx.Steps.Add(builder.Invoke ctx)
        ctx

    member inline this.Combine([<InlineIfLambda>] builder: BuildStep, ctx: StageContext) = this.Combine(ctx, builder)


    member inline _.For(_: StageContext, [<InlineIfLambda>] fn: unit -> StageContext) = fn ()

    member inline _.For(ctx: StageContext, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        ctx.IsActive <- fn().Invoke(ctx)
        ctx

    member inline _.For(ctx: StageContext, [<InlineIfLambda>] fn: unit -> BuildStep) =
        ctx.Steps.Add(fn().Invoke ctx)
        ctx


    /// Add or override environment variables
    [<CustomOperation("envArgs")>]
    member inline _.envArgs(ctx: StageContext, kvs: seq<string * string>) =
        kvs |> Seq.iter (fun (k, v) -> ctx.EnvVars[ k ] <- v)
        ctx

    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeout")>]
    member _.timeout(ctx: StageContext, seconds: int) =
        ctx.Timeout <- ValueSome(TimeSpan.FromSeconds seconds)
        ctx

    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeout")>]
    member _.timeout(ctx: StageContext, time: TimeSpan) =
        ctx.Timeout <- ValueSome time
        ctx

    /// Set if the steps in current stage should run in parallel
    [<CustomOperation("paralle")>]
    member _.paralle(ctx: StageContext, ?value: bool) =
        ctx.IsParallel <- fun () -> defaultArg value true
        ctx


    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add(ctx: StageContext, [<InlineIfLambda>] build: BuildStep) =
        ctx.Steps.Add(build.Invoke(ctx))
        ctx

    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add(ctx: StageContext, [<InlineIfLambda>] build: StageContext -> BuildStep) =
        ctx.Steps.Add(
            async {
                let builder = build ctx
                return! builder.Invoke(ctx)
            }
        )
        ctx

    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add(ctx: StageContext, [<InlineIfLambda>] build: StageContext -> Async<BuildStep>) =
        ctx.Steps.Add(
            async {
                let! builder = build ctx
                return! builder.Invoke(ctx)
            }
        )
        ctx


    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, exe: string, args: string) = ctx.AddCommandStep(exe + " " + args)

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, command: string) = ctx.AddCommandStep(command)

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline this.run(ctx: StageContext, step: StageContext -> string) = this.run (ctx, step ctx)

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Async<string>) =
        ctx.Steps.Add(
            async {
                let! commandStr = step ctx
                use outputStream = Console.OpenStandardOutput()
                let command = ctx.BuildCommand(commandStr, outputStream)
                AnsiConsole.MarkupLine $"[green]{commandStr}[/]"
                let! result = command.ExecuteAsync().Task |> Async.AwaitTask
                return result.ExitCode
            }
        )
        ctx


    /// Add a step to run a async.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<unit>) =
        ctx.Steps.Add(
            async {
                do! step
                return 0
            }
        )
        ctx

    /// Add a step to run a async with exit code returned.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<int>) =
        ctx.Steps.Add step
        ctx


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> unit) =
        ctx.Steps.Add(
            async {
                step ctx
                return 0
            }
        )
        ctx

    /// Add a step to run and return an exist code.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> int) =
        ctx.Steps.Add(async { return step ctx })
        ctx


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Async<unit>) =
        ctx.Steps.Add(
            async {
                do! step ctx
                return 0
            }
        )
        ctx

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Async<int>) =
        ctx.Steps.Add(step ctx)
        ctx


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Task) =
        ctx.Steps.Add(
            async {
                do! step ctx |> Async.AwaitTask
                return 0
            }
        )
        ctx

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Task<unit>) =
        ctx.Steps.Add(
            async {
                do! step ctx |> Async.AwaitTask
                return 0
            }
        )
        ctx

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Task<int>) =
        ctx.Steps.Add(step ctx |> Async.AwaitTask)
        ctx


    /// Set if stage is active, or should run.
    [<CustomOperation("when'")>]
    member inline _.when'(ctx: StageContext, value: bool) = ctx.IsActive <- (fun () -> value)


    /// Set if stage is active, or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar(ctx: StageContext, envKey: string, ?envValue: string) =
        ctx.IsActive <- ctx.WhenEnvArg(envKey, defaultArg envValue "")
        ctx

    /// Set if stage is active, or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg(ctx: StageContext, argKey: string, ?argValue: string) =
        ctx.IsActive <- ctx.WhenCmdArg(argKey, defaultArg argValue "")
        ctx

    /// Set if stage is active, or should run by check the git branch name.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch(ctx: StageContext, branch: string) =
        ctx.IsActive <- ctx.WhenBranch branch
        ctx


/// Build a stage
let stage = StageBuilder

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
