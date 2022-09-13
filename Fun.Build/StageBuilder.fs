[<AutoOpen>]
module Fun.Build.StageBuilder

open System
open System.Threading.Tasks
open Spectre.Console
open CliWrap


type StageBuilder(name: string) =

    member _.Yield(_: unit) = StageContext name

    member _.Delay(fn: unit -> StageContext) = fn ()


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

        ctx.PipelineContext
        |> Option.iter (fun pipeline -> command <- command.WithEnvironmentVariables(pipeline.EnvVars).WithArguments(pipeline.CmdArgs))


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
    member inline _.whenEnvVar(ctx: StageContext, envKey: string, envValue: string) =
        ctx.IsActive <-
            fun () ->
                match ctx.PipelineContext with
                | None -> false
                | Some pipeline -> pipeline.EnvVars.ContainsKey envKey && (envValue = "" || pipeline.EnvVars[envKey] = envValue)
        ctx

    [<CustomOperation("whenEnvVar")>]
    member inline this.whenEnvVar(ctx: StageContext, envKey: string) = this.whenEnvVar (ctx, envKey, "")

    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg(ctx: StageContext, argKey: string, argValue: string) =
        ctx.IsActive <-
            fun () ->
                match ctx.PipelineContext with
                | None -> false
                | Some pipeline ->
                    let index = pipeline.CmdArgs.IndexOf argKey
                    index > -1 && (argValue = "" || (pipeline.CmdArgs.Count > index + 1 && pipeline.CmdArgs[index + 1] = argValue))

        ctx

    [<CustomOperation("whenCmdArg")>]
    member inline this.whenCmdArg(ctx: StageContext, argKey: string) = this.whenCmdArg (ctx, argKey, "")


let stage = StageBuilder
