[<AutoOpen>]
module Fun.Build.StageContextExtensions

open System
open Spectre.Console
open CliWrap
open System.Diagnostics


type StageContext with

    static member Create(name: string) = {
        Name = name
        IsActive = fun _ -> true
        IsParallel = false
        Timeout = ValueNone
        TimeoutForStep = ValueNone
        WorkingDir = ValueNone
        EnvVars = Map.empty
        ParentContext = ValueNone
        Steps = []
    }


    member ctx.GetNamePath() =
        ctx.ParentContext
        |> ValueOption.map (
            function
            | StageParent.Stage x -> x.GetNamePath() + "/"
            | StageParent.Pipeline _ -> ""
        )
        |> ValueOption.defaultValue ""
        |> fun x -> x + ctx.Name


    member ctx.GetWorkingDir() =
        ctx.WorkingDir
        |> ValueOption.defaultWithVOption (fun _ ->
            ctx.ParentContext
            |> ValueOption.bind (
                function
                | StageParent.Stage x -> x.GetWorkingDir()
                | StageParent.Pipeline x -> x.WorkingDir
            )
        )

    member ctx.GetTimeoutForStage() =
        ctx.Timeout
        |> ValueOption.map (fun x -> int x.TotalMilliseconds)
        |> ValueOption.defaultWithVOption (fun _ ->
            ctx.ParentContext
            |> ValueOption.bind (
                function
                | StageParent.Stage x -> x.GetTimeoutForStage() |> ValueSome
                | StageParent.Pipeline x -> x.TimeoutForStage |> ValueOption.map (fun x -> int x.TotalMilliseconds)
            )
        )
        |> ValueOption.defaultValue -1

    member ctx.GetTimeoutForStep() =
        ctx.TimeoutForStep
        |> ValueOption.map (fun x -> int x.TotalMilliseconds)
        |> ValueOption.defaultWithVOption (fun _ ->
            ctx.ParentContext
            |> ValueOption.bind (
                function
                | StageParent.Stage x -> x.GetTimeoutForStep() |> ValueSome
                | StageParent.Pipeline x -> x.TimeoutForStep |> ValueOption.map (fun x -> int x.TotalMilliseconds)
            )
        )
        |> ValueOption.defaultValue -1


    member ctx.BuildEnvVars() =
        let vars = Collections.Generic.Dictionary()

        ctx.ParentContext
        |> ValueOption.map (
            function
            | StageParent.Stage x -> x.BuildEnvVars()
            | StageParent.Pipeline x -> x.EnvVars
        )
        |> ValueOption.iter (fun kvs ->
            for KeyValue (k, v) in kvs do
                vars[k] <- v
        )

        for KeyValue (k, v) in ctx.EnvVars do
            vars[k] <- v

        vars |> Seq.map (fun (KeyValue (k, v)) -> k, v) |> Map.ofSeq


    member ctx.TryGetEnvVar(key: string) =
        ctx.EnvVars
        |> Map.tryFind key
        |> ValueOption.ofOption
        |> ValueOption.defaultWithVOption (fun _ ->
            ctx.ParentContext
            |> ValueOption.map (
                function
                | StageParent.Stage x -> x.TryGetEnvVar key
                | StageParent.Pipeline x -> x.EnvVars |> Map.tryFind key |> ValueOption.ofOption
            )
            |> ValueOption.defaultValue ValueNone
        )

    // If not find then return ""
    member inline ctx.GetEnvVar(key: string) = ctx.TryGetEnvVar key |> ValueOption.defaultValue ""


    member ctx.TryGetCmdArg(key: string) =
        ctx.ParentContext
        |> ValueOption.bind (
            function
            | StageParent.Stage x -> x.TryGetCmdArg key
            | StageParent.Pipeline x ->
                match x.CmdArgs |> List.tryFindIndex ((=) key) with
                | Some index ->
                    if List.length x.CmdArgs > index + 1 then
                        ValueSome x.CmdArgs[index + 1]
                    else
                        ValueSome ""
                | _ -> ValueNone
        )

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

        let isActive = stage.IsActive stage
        let namePath = stage.GetNamePath()

        if isActive then
            let stageSW = Stopwatch.StartNew()
            let isParallel = stage.IsParallel
            let timeoutForStep = stage.GetTimeoutForStep()
            let timeoutForStage = stage.GetTimeoutForStage()

            use cts = new Threading.CancellationTokenSource(timeoutForStage)
            use linkedCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancelToken)


            AnsiConsole.Write(Rule())
            AnsiConsole.Write(
                match index with
                | ValueSome i ->
                    Rule($"STAGE #{i} [bold teal]{namePath}[/] started. Stage timeout: {timeoutForStage}ms. Step timeout: {timeoutForStep}ms.")
                        .LeftAligned()
                | _ -> Rule($"SUB-STAGE {namePath}. Stage timeout: {timeoutForStage}ms. Step timeout: {timeoutForStep}ms.").LeftAligned()
            )
            AnsiConsole.WriteLine()


            let steps =
                stage.Steps
                |> Seq.map (fun step ->
                    let ts = async {
                        let sw = Stopwatch.StartNew()
                        AnsiConsole.MarkupLine $"""[grey]> step started{if isParallel then " in parallel -->" else ":"}[/]"""
                        let! result =
                            match step with
                            | StepFn fn -> fn stage
                            | StepOfStage subStage -> async {
                                let subStage =
                                    { subStage with
                                        ParentContext = ValueSome(StageParent.Stage stage)
                                    }
                                return subStage.Run(ValueNone, linkedCTS.Token)
                              }

                        AnsiConsole.MarkupLine
                            $"""[gray]> step finished{if isParallel then " in parallel." else "."} {sw.ElapsedMilliseconds}ms.[/]"""
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
                | ValueSome i ->
                    Rule($"""STAGE #{i} [bold {if exitCode <> 0 then "red" else "teal"}]{namePath}[/] finished. {stageSW.ElapsedMilliseconds}ms.""")
                        .LeftAligned()
                | _ ->
                    Rule($"""SUB-STAGE [bold {if exitCode <> 0 then "red" else "teal"}]{namePath}[/] finished. {stageSW.ElapsedMilliseconds}ms.""")
                        .LeftAligned()
            )
            AnsiConsole.Write(Rule())

        else
            AnsiConsole.Write(Rule())
            AnsiConsole.MarkupLine(
                match index with
                | ValueSome i -> $"STAGE #{i} [bold grey]{namePath}[/] is inactive"
                | _ -> $"SUB-STAGE [bold grey]{namePath}[/] is inactive"
            )
            AnsiConsole.Write(Rule())

        exitCode
