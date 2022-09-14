namespace rec Fun.Build

open System
open System.Text
open System.Diagnostics
open Spectre.Console
open CliWrap


type StageContext(name: string) =
    let envVars = System.Collections.Generic.Dictionary<string, string>()

    member val Name = name

    member val IsActive = fun () -> true with get, set
    member val IsParallel = fun () -> false with get, set

    member val Timeout: TimeSpan voption = ValueNone with get, set
    member val WorkingDir: string voption = ValueNone with get, set

    member val EnvVars = envVars

    member val PipelineContext: ValueOption<PipelineContext> = ValueNone with get, set

    member val Steps = System.Collections.Generic.List<Async<int>>()


    member inline ctx.GetWorkingDir() =
        if ctx.WorkingDir.IsSome then
            ctx.WorkingDir
        else
            ctx.PipelineContext |> ValueOption.bind (fun x -> x.WorkingDir)


    member inline ctx.BuildEnvVars() =
        let vars = System.Collections.Generic.Dictionary()

        ctx.PipelineContext
        |> ValueOption.iter (fun pipeline ->
            for KeyValue (k, v) in pipeline.EnvVars do
                vars[k] <- v
        )

        for KeyValue (k, v) in ctx.EnvVars do
            vars[k] <- v

        vars


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

    // If not find then return ""
    member inline ctx.GetEnvVar(key: string) = ctx.TryGetEnvVar key |> ValueOption.defaultValue ""


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


    member inline ctx.BuildCommand(commandStr: string, outputStream: IO.Stream) =
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

    member inline ctx.AddCommandStep(commandStr: string) =
        ctx.Steps.Add(
            async {
                use outputStream = Console.OpenStandardOutput()
                let command = ctx.BuildCommand(commandStr, outputStream)
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


type PipelineContext() =
    let cmdArgs = System.Collections.Generic.List<string>(Environment.GetCommandLineArgs())
    let envVars = System.Collections.Generic.Dictionary<string, string>()

    do
        for key in Environment.GetEnvironmentVariables().Keys do
            let key = string key
            try
                envVars.Add(key, Environment.GetEnvironmentVariable key)
            with _ ->
                envVars.Add(key, "")


    member val Name = "" with get, set

    member val CmdArgs: System.Collections.Generic.List<string> = cmdArgs
    member val EnvVars: System.Collections.Generic.Dictionary<string, string> = envVars

    member val Timeout: TimeSpan voption = ValueNone with get, set
    member val WorkingDir: string voption = ValueNone with get, set

    member val Stages = System.Collections.Generic.List<StageContext>()
    member val PostStages = System.Collections.Generic.List<StageContext>()


    member this.RunStages(stages: StageContext seq, ?failfast: bool) =
        let failfast = defaultArg failfast true
        let stages = stages |> Seq.filter (fun x -> x.IsActive()) |> Seq.toList
        let mutable i = 0
        let mutable shouldStop = false

        while i < stages.Length && (not failfast || not shouldStop) do
            let stage = stages[i]

            let timeout =
                match stage.Timeout, this.Timeout with
                | ValueSome t, _
                | _, ValueSome t -> int t.TotalMilliseconds
                | _ -> 0

            AnsiConsole.Write(Rule())
            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] started").LeftAligned())
            AnsiConsole.WriteLine()

            let isParallel = stage.IsParallel()

            let steps =
                stage.Steps
                |> Seq.map (fun step -> async {
                    AnsiConsole.MarkupLine $"""[grey]> start step{if isParallel then " in parallel -->" else ":"}[/]"""
                    let! result = step
                    AnsiConsole.MarkupLine $"""[gray]> finished run step{if isParallel then " in parallel." else "."}[/]"""
                    AnsiConsole.WriteLine()
                    if result <> 0 then
                        failwith $"Step finished without a success exist code. {result}"
                }
                )

            try
                if isParallel then
                    let steps = steps |> Async.Parallel
                    if timeout > 0 then
                        Async.RunSynchronously(steps, timeout = timeout) |> ignore
                    else
                        Async.RunSynchronously(steps) |> ignore
                else if timeout > 0 then
                    steps |> Seq.iter (fun step -> Async.RunSynchronously(step, timeout = timeout) |> ignore)
                else
                    steps |> Seq.iter (Async.RunSynchronously >> ignore)
            with ex ->
                AnsiConsole.MarkupLine $"[red]> Run step failed: {ex.Message}[/]"
                AnsiConsole.WriteException ex
                AnsiConsole.WriteLine()
                shouldStop <- true

            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] finished").LeftAligned())
            AnsiConsole.Write(Rule())

            i <- i + 1


    member this.Run() =
        Console.InputEncoding <- Encoding.UTF8
        Console.OutputEncoding <- Encoding.UTF8

        if String.IsNullOrEmpty this.Name |> not then
            let title = FigletText this.Name
            title.LeftAligned() |> ignore
            title.Color <- Color.Red
            AnsiConsole.Write title

        AnsiConsole.MarkupLine $"[bold lime]Run PIPELINE {this.Name}[/]"
        AnsiConsole.WriteLine()

        let sw = Stopwatch.StartNew()


        AnsiConsole.MarkupLine $"[grey]Run stages[/]"
        this.RunStages(this.Stages, failfast = true)
        AnsiConsole.MarkupLine $"[grey]Run stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()

        AnsiConsole.MarkupLine $"[grey]Run post stages[/]"
        this.RunStages(this.PostStages, failfast = false)
        AnsiConsole.MarkupLine $"[grey]Run post stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()


        AnsiConsole.MarkupLine $"[bold lime]Run PIPELINE {this.Name} finished in {sw.ElapsedMilliseconds} ms[/]"
        AnsiConsole.WriteLine()


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext

type BuildConditions = delegate of ctx: StageContext * conditions: (unit -> bool) list -> (unit -> bool) list

type BuildStageIsActive = delegate of ctx: StageContext -> (unit -> bool)
