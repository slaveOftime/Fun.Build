namespace rec Fun.Build

open System
open System.Text
open System.Linq
open System.Diagnostics
open Spectre.Console
open CliWrap


type StageContext =
    {
        Name: string
        IsActive: StageContext -> bool
        IsParallel: bool
        Timeout: TimeSpan voption
        WorkingDir: string voption
        EnvVars: Map<string, string>
        PipelineContext: ValueOption<PipelineContext>
        Steps: (StageContext -> Async<int>) list
    }

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


type PipelineContext =
    {
        Name: string
        CmdArgs: string list
        EnvVars: Map<string, string>
        Timeout: TimeSpan voption
        WorkingDir: string voption
        Stages: StageContext list
        PostStages: StageContext list
    }

    static member Create(name: string) =
        let envVars = System.Collections.Generic.Dictionary<string, string>()

        for key in Environment.GetEnvironmentVariables().Keys do
            let key = string key
            try
                envVars.Add(key, Environment.GetEnvironmentVariable key)
            with _ ->
                envVars.Add(key, "")

        {
            Name = name
            CmdArgs = Seq.toList (Environment.GetCommandLineArgs())
            EnvVars = envVars |> Seq.map (fun (KeyValue (k, v)) -> k, v) |> Map.ofSeq
            Timeout = ValueNone
            WorkingDir = ValueNone
            Stages = []
            PostStages = []
        }


    member this.RunStages(stages: StageContext seq, ?failfast: bool) =
        let failfast = defaultArg failfast true

        let stages =
            stages
            |> Seq.map (fun x -> { x with PipelineContext = ValueSome this })
            |> Seq.filter (fun x -> x.IsActive x)
            |> Seq.toList

        let mutable i = 0
        let mutable hasError = false

        while i < stages.Length && (not failfast || not hasError) do
            let stage = stages[i]

            let timeout =
                match stage.Timeout, this.Timeout with
                | ValueSome t, _
                | _, ValueSome t -> int t.TotalMilliseconds
                | _ -> 0

            AnsiConsole.Write(Rule())
            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] started").LeftAligned())
            AnsiConsole.WriteLine()

            let isParallel = stage.IsParallel

            let steps =
                stage.Steps
                |> Seq.map (fun step -> async {
                    AnsiConsole.MarkupLine $"""[grey]> start step{if isParallel then " in parallel -->" else ":"}[/]"""
                    let! result = step stage
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
                hasError <- true

            AnsiConsole.Write(Rule($"STAGE #{i} [bold teal]{stage.Name}[/] finished").LeftAligned())
            AnsiConsole.Write(Rule())

            i <- i + 1

        hasError


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
        let hasFailedStage = this.RunStages(this.Stages, failfast = true)
        AnsiConsole.MarkupLine $"[grey]Run stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()

        AnsiConsole.MarkupLine $"[grey]Run post stages[/]"
        let hasFailedPostStage = this.RunStages(this.PostStages, failfast = false)
        AnsiConsole.MarkupLine $"[grey]Run post stages finished[/]"
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()


        AnsiConsole.MarkupLine $"[bold lime]Run PIPELINE {this.Name} finished in {sw.ElapsedMilliseconds} ms[/]"
        AnsiConsole.WriteLine()

        if hasFailedStage || hasFailedPostStage then failwith "Pipeline is failed."


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext

type BuildConditions = delegate of conditions: (StageContext -> bool) list -> (StageContext -> bool) list

type BuildStageIsActive = delegate of ctx: StageContext -> bool

type BuildStep = delegate of ctx: StageContext -> Async<int>
