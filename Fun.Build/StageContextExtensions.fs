[<AutoOpen>]
module Fun.Build.StageContextExtensions

open System
open Spectre.Console
open CliWrap


type StageContext with

    static member Create(name: string) = {
        Name = name
        IsActive = fun _ -> true
        IsParallel = false
        Timeout = ValueNone
        TimeoutForStep = ValueNone
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

    member ctx.GetTimeoutForStage() =
        match ctx.Timeout with
        | ValueSome x -> int x.TotalMilliseconds
        | _ ->
            ctx.PipelineContext
            |> ValueOption.bind (fun x -> x.TimeoutForStage)
            |> ValueOption.map (fun x -> int x.TotalMilliseconds)
            |> ValueOption.defaultValue -1

    member ctx.GetTimeoutForStep() =
        match ctx.TimeoutForStep with
        | ValueSome x -> int x.TotalMilliseconds
        | _ ->
            ctx.PipelineContext
            |> ValueOption.bind (fun x -> x.TimeoutForStep)
            |> ValueOption.map (fun x -> int x.TotalMilliseconds)
            |> ValueOption.defaultValue -1


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
        | ValueNone -> ValueNone
        | ValueSome pipeline ->
            match pipeline.CmdArgs |> List.tryFindIndex ((=) key) with
            | Some index ->
                if List.length pipeline.CmdArgs > index + 1 then
                    ValueSome pipeline.CmdArgs[index + 1]
                else
                    ValueSome ""
            | _ -> ValueNone

    member inline ctx.GetCmdArg(key) = ctx.TryGetCmdArg key |> ValueOption.defaultValue ""


    member inline ctx.TryGetCmdArgOrEnvVar(key: string) =
        match ctx.TryGetCmdArg(key) with
        | ValueSome x -> ValueSome x
        | _ -> ctx.TryGetEnvVar(key)

    member inline ctx.GetCmdArgOrEnvVar(key) = ctx.TryGetCmdArgOrEnvVar key |> ValueOption.defaultValue ""


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
                    StepFn(fun ctx -> async {
                        use outputStream = Console.OpenStandardOutput()
                        let command = ctx.BuildCommand(commandStr, outputStream)
                        AnsiConsole.MarkupLine $"[green]{command.ToString()}[/]"
                        let! result = command.ExecuteAsync().Task |> Async.AwaitTask
                        return result.ExitCode
                    }
                    )
                ]
        }


    member ctx.WhenEnvArg(envKey: string, envValue: string) =
        match ctx.TryGetEnvVar envKey with
        | ValueSome v when envValue = "" || v = envValue -> true
        | _ -> false

    member ctx.WhenCmdArg(argKey: string, argValue: string) =
        match ctx.TryGetCmdArg argKey with
        | ValueSome v when argValue = "" || v = argValue -> true
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


    /// Run the stage. If index is not provided then it will be treated as sub-stage.
    member stage.Run(index: int voption, cancelToken: Threading.CancellationToken) =
        let mutable exitCode = 0

        let isParallel = stage.IsParallel
        let timeoutForStep = stage.GetTimeoutForStep()
        let timeoutForStage = stage.GetTimeoutForStage()

        use cts = new Threading.CancellationTokenSource(timeoutForStage)
        use linkedCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancelToken)


        AnsiConsole.Write(Rule())
        AnsiConsole.Write(
            match index with
            | ValueSome i ->
                Rule($"STAGE #{i} [bold teal]{stage.Name}[/] started. Stage timeout: {timeoutForStage}ms. Step timeout: {timeoutForStep}ms.")
                    .LeftAligned()
            | _ ->
                Rule($"> step started: sub-stage {stage.Name}. Stage timeout: {timeoutForStage}ms. Step timeout: {timeoutForStep}ms.")
                    .LeftAligned()
        )
        AnsiConsole.WriteLine()


        let steps =
            stage.Steps
            |> Seq.map (fun step ->
                let ts = async {
                    AnsiConsole.MarkupLine $"""[grey]> step started{if isParallel then " in parallel -->" else ":"}[/]"""
                    let! result =
                        match step with
                        | StepFn fn -> fn stage
                        | StepOfStage subStage -> async { return subStage.Run(ValueNone, linkedCTS.Token) }

                    AnsiConsole.MarkupLine $"""[gray]> step finished{if isParallel then " in parallel." else "."}[/]"""
                    AnsiConsole.WriteLine()
                    if result <> 0 then
                        failwith $"Step finished without a success exist code. {result}"
                }
                Async.StartChild(ts, timeoutForStep)
            )

        try
            let ts =
                if isParallel then
                    async {
                        let! completers = steps |> Async.Parallel
                        do! Async.Parallel completers |> Async.Ignore
                    }
                else
                    async {
                        for step in steps do
                            let! completer = step
                            do! completer
                    }
            Async.RunSynchronously(ts, cancellationToken = linkedCTS.Token)

        with ex ->
            AnsiConsole.MarkupLine $"[red]> step failed: {ex.Message}[/]"
            AnsiConsole.WriteException ex
            AnsiConsole.WriteLine()
            exitCode <- -1

        AnsiConsole.Write(
            match index with
            | ValueSome i -> Rule($"""STAGE #{i} [bold {if exitCode <> 0 then "red" else "teal"}]{stage.Name}[/] finished""").LeftAligned()
            | _ -> Rule($"""> step finished: sub-stage [bold {if exitCode <> 0 then "red" else "teal"}]{stage.Name}[/].""").LeftAligned()
        )
        AnsiConsole.Write(Rule())

        exitCode
