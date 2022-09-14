[<AutoOpen>]
module Fun.Build.StageContextExtensions

open System
open System.Text
open System.Linq
open System.Diagnostics
open Spectre.Console
open CliWrap


type StageContext with

    static member Create(name: string) = {
        Name = name
        IsActive = fun _ -> true
        IsParallel = false
        Timeout = ValueNone
        WorkingDir = ValueNone
        EnvVars = Map.empty
        PipelineContext = ValueNone
        Steps = []
    }


    member ctx.GetWorkingDir() =
        if ctx.WorkingDir.IsSome then
            ctx.WorkingDir
        else
            ctx.PipelineContext |> ValueOption.bind (fun x -> x.WorkingDir)


    member ctx.BuildEnvVars() =
        let vars = Collections.Generic.Dictionary()

        ctx.PipelineContext
        |> ValueOption.iter (fun pipeline ->
            for KeyValue (k, v) in pipeline.EnvVars do
                vars[k] <- v
        )

        for KeyValue (k, v) in ctx.EnvVars do
            vars[k] <- v

        vars |> Seq.map (fun (KeyValue (k, v)) -> k, v) |> Map.ofSeq


    member ctx.TryGetEnvVar(key: string) =
        if ctx.EnvVars.ContainsKey key then
            ValueSome ctx.EnvVars[key]
        else
            ctx.PipelineContext
            |> ValueOption.bind (fun pipeline ->
                if pipeline.EnvVars.ContainsKey key then
                    ValueSome pipeline.EnvVars[key]
                else
                    ValueNone
            )

    // If not find then return ""
    member inline ctx.GetEnvVar(key: string) = ctx.TryGetEnvVar key |> ValueOption.defaultValue ""


    member ctx.TryGetCmdArg(key: string) =
        match ctx.PipelineContext with
        | ValueNone -> None
        | ValueSome pipeline ->
            match pipeline.CmdArgs |> List.tryFindIndex ((=) key) with
            | Some index ->
                if List.length pipeline.CmdArgs > index + 1 then
                    Some pipeline.CmdArgs[index + 1]
                else
                    Some ""
            | _ -> None


    member ctx.BuildCommand(commandStr: string, outputStream: IO.Stream) =
        let index = commandStr.IndexOf " "

        let cmd, args =
            if index > 0 then
                let cmd = commandStr.Substring(0, index)
                let args = commandStr.Substring(index + 1)
                cmd, args
            else
                commandStr, ""

        let mutable command = Cli.Wrap(cmd).WithArguments(args)

        ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command <- command.WithWorkingDirectory x)

        command <- command.WithEnvironmentVariables(ctx.BuildEnvVars())
        command <- command.WithStandardOutputPipe(PipeTarget.ToStream outputStream).WithValidation(CommandResultValidation.None)
        command

    member ctx.AddCommandStep(commandStr: string) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    fun ctx -> async {
                        use outputStream = Console.OpenStandardOutput()
                        let command = ctx.BuildCommand(commandStr, outputStream)
                        AnsiConsole.MarkupLine $"[green]{command.ToString()}[/]"
                        let! result = command.ExecuteAsync().Task |> Async.AwaitTask
                        return result.ExitCode
                    }
                ]
        }


    member ctx.WhenEnvArg(envKey: string, envValue: string) =
        match ctx.TryGetEnvVar envKey with
        | ValueSome v when envValue = "" || v = envValue -> true
        | _ -> false

    member ctx.WhenCmdArg(argKey: string, argValue: string) =
        match ctx.TryGetCmdArg argKey with
        | Some v when argValue = "" || v = argValue -> true
        | _ -> false


    member ctx.WhenBranch(branch: string) =
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
