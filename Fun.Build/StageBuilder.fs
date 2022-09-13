[<AutoOpen>]
module Fun.Build.StageBuilder

open System
open System.Threading.Tasks
open Spectre.Console
open CliWrap


type StageContext with

    member inline ctx.GetWorkingDir() =
        if ctx.WorkingDir.IsSome then
            ctx.WorkingDir
        else
            ctx.PipelineContext |> ValueOption.bind (fun x -> x.WorkingDir)


    member inline ctx.TryGetEnvVar(key: string) =
        if ctx.EnvVars.ContainsKey key then
            ctx.EnvVars[key] |> ValueSome
        else
            ctx.PipelineContext
            |> ValueOption.bind (fun pipeline ->
                if pipeline.EnvVars.ContainsKey key then
                    ValueSome pipeline.EnvVars[key]
                else
                    ValueNone
            )


    member inline ctx.TryGetCmdArg(key: string) =
        match ctx.PipelineContext with
        | ValueNone -> None
        | ValueSome pipeline ->
            let index = pipeline.CmdArgs.IndexOf key
            if index > -1 then
                if pipeline.CmdArgs.Count > index + 1 then
                    Some pipeline.CmdArgs[index + 1]
                else
                    Some ""
            else
                None


    member inline ctx.AddCommandStep(exe: string, args: string) =
        let mutable command = Cli.Wrap(exe).WithArguments(args)

        ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command <- command.WithWorkingDirectory x)

        //ctx.PipelineContext |> Option.iter (fun pipeline -> command <- command.WithEnvironmentVariables(pipeline.EnvVars))
        command <- command.WithEnvironmentVariables(ctx.EnvVars)

        ctx.Steps.Add(
            async {
                use output = Console.OpenStandardOutput()

                command <- command.WithStandardOutputPipe(PipeTarget.ToStream output).WithValidation(CommandResultValidation.None)

                AnsiConsole.MarkupLine $"[green]{command.ToString()}[/]"
                let! result = command.ExecuteAsync().Task |> Async.AwaitTask
                return result.ExitCode
            }
        )

        ctx


    member ctx.WhenEnvArg(envKey: string, envValue: string) =
        fun () ->
            match ctx.TryGetEnvVar envKey with
            | ValueSome v when envValue = "" || v = envValue -> true
            | _ -> false

    member ctx.WhenCmdArg(argKey: string, argValue: string) =
        fun () ->
            match ctx.TryGetCmdArg argKey with
            | Some v when argValue = "" || v = argValue -> true
            | _ -> false

    member ctx.WhenBranch(branch: string) =
        fun () ->
            try
                let mutable currentBranch = ""

                let mutable command =
                    Cli
                        .Wrap("git")
                        .WithArguments("branch --show-current")
                        .WithStandardOutputPipe(PipeTarget.ToDelegate(fun x -> currentBranch <- x))
                        .WithValidation(CommandResultValidation.None)

                ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command <- command.WithWorkingDirectory x)

                command.ExecuteAsync().GetAwaiter().GetResult() |> ignore

                currentBranch = branch

            with ex ->
                AnsiConsole.MarkupLine $"[red]Run git to get branch info failed: {ex.Message}[/]"
                false


type ConditionsBuilder() =

    member inline _.Yield(_: unit) = BuildConditions(fun _ x -> x)

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildConditions) = BuildConditions(fun ctx conds -> fn().Invoke(ctx, conds))

    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, ?envValue: string) =
        BuildConditions(fun ctx conditions -> builder.Invoke(ctx, conditions) @ [ ctx.WhenEnvArg(envKey, defaultArg envValue "") ])

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKey: string, ?argValue: string) =
        BuildConditions(fun ctx conditions -> builder.Invoke(ctx, conditions) @ [ ctx.WhenCmdArg(argKey, defaultArg argValue "") ])

    [<CustomOperation("branch")>]
    member inline _.branch([<InlineIfLambda>] builder: BuildConditions, branch: string) =
        BuildConditions(fun ctx conditions -> builder.Invoke(ctx, conditions) @ [ ctx.WhenBranch(branch) ])


type WhenAnyBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun stage -> stage.IsActive <- fun () -> builder.Invoke(stage, []) |> Seq.exists (fun fn -> fn ()))


type WhenAllBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun stage ->
            stage.IsActive <- fun () -> builder.Invoke(stage, []) |> Seq.map (fun fn -> fn ()) |> Seq.reduce (fun x y -> x && y)
        )


type StageBuilder(name: string) =

    member _.Yield(_: unit) = StageContext name

    member inline _.Yield([<InlineIfLambda>] condition: BuildStageIsActive) = condition


    member inline _.Delay(fn: unit -> StageContext) = fn ()

    member _.Delay(fn: unit -> BuildStageIsActive) =
        let ctx = StageContext name
        let condition = fn ()
        condition.Invoke ctx
        ctx


    member inline _.Combine(ctx: StageContext, [<InlineIfLambda>] condition: BuildStageIsActive) =
        condition.Invoke ctx
        ctx

    member inline this.Combine([<InlineIfLambda>] condition: BuildStageIsActive, ctx: StageContext) = this.Combine(ctx, condition)


    member inline _.For(_: StageContext, [<InlineIfLambda>] fn: unit -> StageContext) = fn ()


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
    member _.paralle(ctx: StageContext, value: bool) =
        ctx.IsParallel <- fun () -> value
        ctx


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, exe: string, args: string) = ctx.AddCommandStep(exe, args)

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, command: string) =
        let index = command.IndexOf " "

        if index > 0 then
            let cmd = command.Substring(0, index)
            let args = command.Substring(index + 1)
            ctx.AddCommandStep(cmd, args)
        else
            ctx.AddCommandStep(command, "")


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Task) =
        ctx.Steps.Add(
            async {
                do! Async.AwaitTask step
                return 0
            }
        )
        ctx

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Task<int>) =
        ctx.Steps.Add(Async.AwaitTask step)
        ctx


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<unit>) =
        ctx.Steps.Add(
            async {
                do! step
                return 0
            }
        )
        ctx

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<int>) =
        ctx.Steps.Add step
        ctx


    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: unit -> unit) =
        ctx.Steps.Add(
            async {
                step ()
                return 0
            }
        )
        ctx

    /// Add a step to run.
    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: unit -> int) =
        ctx.Steps.Add(async { return step () })
        ctx


    /// Add a step to run.
    [<CustomOperation("runWith")>]
    member inline _.runWith(ctx: StageContext, step: StageContext -> Async<int>) =
        ctx.Steps.Add(async { return! step ctx })
        ctx

    /// Add a step to run.
    [<CustomOperation("runWith")>]
    member inline _.runWith(ctx: StageContext, step: StageContext -> Async<unit>) =
        ctx.Steps.Add(
            async {
                do! step ctx
                return 0
            }
        )
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
/// When any of the added conditions are satisified, the stage will be active
let whenAny = WhenAnyBuilder()
/// When all of the added conditions are satisified, the stage will be active
let whenAll = WhenAllBuilder()
