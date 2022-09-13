[<AutoOpen>]
module Fun.Build.StageBuilder

open System
open System.Threading.Tasks
open Spectre.Console
open CliWrap


type StageContext with

    member ctx.WhenEnvArg(envKey: string, envValue: string) =
        fun () ->
            match ctx.PipelineContext with
            | None -> false
            | Some pipeline -> pipeline.EnvVars.ContainsKey envKey && (envValue = "" || pipeline.EnvVars[envKey] = envValue)

    member ctx.WhenCmdArg(argKey: string, argValue: string) =
        fun () ->
            match ctx.PipelineContext with
            | None -> false
            | Some pipeline ->
                let index = pipeline.CmdArgs.IndexOf argKey
                index > -1 && (argValue = "" || (pipeline.CmdArgs.Count > index + 1 && pipeline.CmdArgs[index + 1] = argValue))

    member ctx.WhenBranch(branch: string) =
        fun () ->
            try
                let mutable str = ""
                let _ =
                    Cli
                        .Wrap("git")
                        .WithArguments("branch --show-current")
                        .WithStandardOutputPipe(PipeTarget.ToDelegate(fun x -> str <- x))
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteAsync()
                        .GetAwaiter()
                        .GetResult()
                str = branch
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


    member inline _.For(ctx: StageContext, [<InlineIfLambda>] fn: unit -> StageContext) = fn ()


    [<CustomOperation("timeout")>]
    member _.timeout(ctx: StageContext, seconds: int) =
        ctx.Timeout <- TimeSpan.FromSeconds seconds
        ctx

    [<CustomOperation("parall")>]
    member _.parall(ctx: StageContext, value: bool) =
        ctx.IsParallel <- fun () -> value
        ctx


    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, exe: string, args: string) =
        let mutable command = Cli.Wrap(exe).WithArguments(args)

        ctx.PipelineContext |> Option.iter (fun pipeline -> command <- command.WithEnvironmentVariables(pipeline.EnvVars))

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

    [<CustomOperation("run")>]
    member inline this.run(ctx: StageContext, command: string) =
        let index = command.IndexOf " "

        if index > 0 then
            let cmd = command.Substring(0, index)
            let args = command.Substring(index + 1)
            this.run (ctx, cmd, args)
        else
            this.run (ctx, command, "")


    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Task) =
        ctx.Steps.Add(
            async {
                do! Async.AwaitTask step
                return 0
            }
        )
        ctx

    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Task<int>) =
        ctx.Steps.Add(Async.AwaitTask step)
        ctx


    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<unit>) =
        ctx.Steps.Add(
            async {
                do! step
                return 0
            }
        )
        ctx

    [<CustomOperation("run")>]
    member inline _.run(ctx: StageContext, step: Async<int>) =
        ctx.Steps.Add step
        ctx


    [<CustomOperation("when'")>]
    member inline _.when'(ctx: StageContext, value: bool) = ctx.IsActive <- (fun () -> value)

    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar(ctx: StageContext, envKey: string, ?envValue: string) =
        ctx.IsActive <- ctx.WhenEnvArg(envKey, defaultArg envValue "")
        ctx

    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg(ctx: StageContext, argKey: string, ?argValue: string) =
        ctx.IsActive <- ctx.WhenCmdArg(argKey, defaultArg argValue "")
        ctx

    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch(ctx: StageContext, branch: string) =
        ctx.IsActive <- ctx.WhenBranch branch
        ctx


let stage = StageBuilder
let whenAny = WhenAnyBuilder()
let whenAll = WhenAllBuilder()
