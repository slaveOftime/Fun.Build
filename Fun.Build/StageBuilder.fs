[<AutoOpen>]
module Fun.Build.StageBuilder

open System
open System.Threading.Tasks
open Spectre.Console


type StageBuilder(name: string) =

    member _.Run(ctx: StageContext) = ctx


    member _.Yield(_: unit) = StageContext.Create name

    member inline _.Yield([<InlineIfLambda>] condition: BuildStageIsActive) = condition
    member inline _.Yield([<InlineIfLambda>] builder: BuildStep) = builder


    member inline _.Delay(fn: unit -> StageContext) = fn ()

    member _.Delay(fn: unit -> BuildStageIsActive) =
        let ctx = StageContext.Create name
        { ctx with IsActive = fn().Invoke }

    member _.Delay(fn: unit -> BuildStep) =
        let ctx = StageContext.Create name
        { ctx with Steps = [ fn().Invoke ] }


    member inline _.Combine(ctx: StageContext, [<InlineIfLambda>] condition: BuildStageIsActive) = { ctx with IsActive = condition.Invoke }

    member inline this.Combine([<InlineIfLambda>] condition: BuildStageIsActive, ctx: StageContext) = this.Combine(ctx, condition)


    member inline _.Combine(ctx: StageContext, [<InlineIfLambda>] builder: BuildStep) = { ctx with Steps = ctx.Steps @ [ builder.Invoke ] }

    member inline this.Combine([<InlineIfLambda>] builder: BuildStep, ctx: StageContext) = this.Combine(ctx, builder)


    member inline _.For(_: StageContext, [<InlineIfLambda>] fn: unit -> StageContext) = fn ()

    member inline _.For(ctx: StageContext, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) = { ctx with IsActive = fn().Invoke }

    member inline _.For(ctx: StageContext, [<InlineIfLambda>] fn: unit -> BuildStep) = { ctx with Steps = ctx.Steps @ [ fn().Invoke ] }


    /// Add or override environment variables
    [<CustomOperation("envArgs")>]
    member inline _.envArgs(ctx: StageContext, kvs: seq<string * string>) =
        { ctx with
            EnvVars = kvs |> Seq.fold (fun state (k, v) -> Map.add k v state) ctx.EnvVars
        }

    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeout")>]
    member _.timeout(ctx: StageContext, seconds: int) =
        { ctx with
            Timeout = ValueSome(TimeSpan.FromSeconds seconds)
        }

    /// Set timeout for every step under the current stage.
    /// Unit is second.
    [<CustomOperation("timeout")>]
    member _.timeout(ctx: StageContext, time: TimeSpan) = { ctx with Timeout = ValueSome time }

    /// Set if the steps in current stage should run in parallel
    [<CustomOperation("paralle")>]
    member _.paralle(ctx: StageContext, ?value: bool) = { ctx with IsParallel = defaultArg value true }


    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add(ctx: StageContext, [<InlineIfLambda>] build: BuildStep) = { ctx with Steps = ctx.Steps @ [ build.Invoke ] }

    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add(ctx: StageContext, [<InlineIfLambda>] build: StageContext -> BuildStep) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun ctx -> async {
                        let builder = build ctx
                        return! builder.Invoke(ctx)
                    }
                ]
        }

    /// Add a step.
    [<CustomOperation("add")>]
    member inline _.add(ctx: StageContext, [<InlineIfLambda>] build: StageContext -> Async<BuildStep>) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun ctx -> async {
                        let! builder = build ctx
                        return! builder.Invoke(ctx)
                    }
                ]
        }


    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, exe: string, args: string) = ctx.AddCommandStep(exe + " " + args)

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, command: string) = ctx.AddCommandStep(command)

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> string) =
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

    /// Add a step to run command. This will not encrypt any sensitive information when print to console.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Async<string>) =
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


    /// Add a step to run a async.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<unit>) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun ctx -> async {
                        do! step
                        return 0
                    }
                ]
        }

    /// Add a step to run a async with exit code returned.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<int>) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun _ -> step
                ]
        }


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> unit) =
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

    /// Add a step to run and return an exist code.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> int) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun _ -> async { return step ctx }
                ]
        }


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Async<unit>) =
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

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Async<int>) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun _ -> async { return! step ctx }
                ]
        }


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Task) =
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

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Task<unit>) =
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

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: StageContext -> Task<int>) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun _ -> async { return! step ctx |> Async.AwaitTask }
                ]
        }


    /// Set if stage is active, or should run.
    [<CustomOperation("when'")>]
    member inline _.when'(ctx: StageContext, value: bool) = { ctx with IsActive = fun _ -> value }


    /// Set if stage is active, or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar(ctx: StageContext, envKey: string, ?envValue: string) =
        { ctx with
            IsActive = fun ctx -> ctx.WhenEnvArg(envKey, defaultArg envValue "")
        }

    /// Set if stage is active, or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg(ctx: StageContext, argKey: string, ?argValue: string) =
        { ctx with
            IsActive = fun ctx -> ctx.WhenCmdArg(argKey, defaultArg argValue "")
        }

    /// Set if stage is active, or should run by check the git branch name.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch(ctx: StageContext, branch: string) = { ctx with IsActive = fun ctx -> ctx.WhenBranch branch }


/// Build a stage
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
