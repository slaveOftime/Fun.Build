[<AutoOpen>]
module Fun.Build.StageContextExtensions

open System
open System.Diagnostics
open Spectre.Console


type StageContext with

    static member Create(name: string) = {
        Name = name
        IsActive = fun _ -> true
        IsParallel = false
        Timeout = ValueNone
        TimeoutForStep = ValueNone
        WorkingDir = ValueNone
        EnvVars = Map.empty
        AcceptableExitCodes = set [| 0 |]
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


    member ctx.Mode =
        match ctx.ParentContext with
        | ValueNone -> Mode.Execution
        | ValueSome (StageParent.Stage s) -> s.Mode
        | ValueSome (StageParent.Pipeline p) -> p.Mode


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


    member inline ctx.BuildStepPrefix(i: int) = sprintf "%s/step-%d>" (ctx.GetNamePath()) i

    member ctx.BuildIndent() =
        match ctx.ParentContext with
        | ValueSome (StageParent.Stage s) -> "    " + s.BuildIndent()
        | ValueNone
        | ValueSome (StageParent.Pipeline _) -> "    "


    /// Verify if the exit code is allowed.
    member stage.IsAcceptableExitCode(exitCode: int) : bool =
        let parentAcceptableExitCodes =
            match stage.ParentContext with
            | ValueNone -> Set.empty
            | ValueSome (StageParent.Pipeline pipeline) -> pipeline.AcceptableExitCodes
            | ValueSome (StageParent.Stage parentStage) -> parentStage.AcceptableExitCodes

        Set.contains exitCode stage.AcceptableExitCodes || Set.contains exitCode parentAcceptableExitCodes

    member stage.MapExitCodeToResult(exitCode: int) =
        if stage.IsAcceptableExitCode exitCode then
            Ok()
        else
            Error "Exit code is not indicating as successful."


    /// Run the stage. If index is not provided then it will be treated as sub-stage.
    member stage.Run(index: StageIndex, cancelToken: Threading.CancellationToken) =
        let mutable isSuccess = true
        let stepExns = ResizeArray<exn>()

        let isActive = stage.IsActive stage
        let namePath = stage.GetNamePath()

        if isActive then
            let stageSW = Stopwatch.StartNew()
            let isParallel = stage.IsParallel
            let timeoutForStep = stage.GetTimeoutForStep()
            let timeoutForStage = stage.GetTimeoutForStage()

            use cts = new Threading.CancellationTokenSource(timeoutForStage)
            use stepErrorCTS = new Threading.CancellationTokenSource()
            use linkedStepErrorCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stepErrorCTS.Token)
            use linkedCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(linkedStepErrorCTS.Token, cancelToken)

            use stepCTS = new Threading.CancellationTokenSource(timeoutForStep)
            use linkedStepCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(stepCTS.Token, linkedCTS.Token)


            AnsiConsole.WriteLine()
            AnsiConsole.Write(
                let extraInfo = $"Stage timeout: {timeoutForStage}ms. Step timeout: {timeoutForStep}ms."
                match index with
                | StageIndex.Stage i -> Rule($"STAGE #{i} [bold teal]{namePath}[/] started. {extraInfo}").LeftAligned()
                | StageIndex.Step i -> Rule($"SUBSTAGE [bold teal]{stage.BuildStepPrefix i}[/]. {extraInfo}").LeftAligned()
            )
            AnsiConsole.WriteLine()


            let steps =
                stage.Steps
                |> Seq.mapi (fun i step -> async {
                    let prefix = stage.BuildStepPrefix i
                    try
                        let sw = Stopwatch.StartNew()
                        AnsiConsole.MarkupLine $"""[turquoise4]{prefix} started{if isParallel then " in parallel -->" else ""}[/]"""

                        let! isSuccess =
                            match step with
                            | Step.StepFn fn -> async {
                                match! fn (stage, i) with
                                | Error e ->
                                    if String.IsNullOrEmpty e |> not then
                                        AnsiConsole.MarkupLine $"""{prefix} error: [red]{e}[/]"""
                                    return false
                                | Ok _ -> return true
                              }
                            | Step.StepOfStage subStage -> async {
                                let subStage =
                                    { subStage with
                                        ParentContext = ValueSome(StageParent.Stage stage)
                                    }
                                let isSuccess, exn = subStage.Run(StageIndex.Step i, linkedStepCTS.Token)
                                stepExns.AddRange exn
                                return isSuccess
                              }

                        AnsiConsole.MarkupLine
                            $"""[turquoise4]{prefix} finished{if isParallel then " in parallel." else "."} {sw.ElapsedMilliseconds}ms.[/]"""
                        AnsiConsole.WriteLine()

                        if not isSuccess then stepErrorCTS.Cancel()
                        return isSuccess

                    with ex ->
                        AnsiConsole.MarkupLine $"[red]{prefix} exception hanppened.[/]"
                        AnsiConsole.WriteException ex
                        stepExns.Add ex
                        stepErrorCTS.Cancel()
                        return false
                }
                )

            try
                let ts =
                    if isParallel then
                        async {
                            let completers = ResizeArray()

                            for ts in steps do
                                let! completer = Async.StartChild(ts, timeoutForStep)
                                completers.Add completer

                            let mutable i = 0
                            while i < completers.Count && isSuccess do
                                let! result = completers[i]
                                i <- i + 1
                                isSuccess <- isSuccess && result
                        }
                    else
                        async {
                            let mutable i = 0
                            let length = Seq.length steps
                            while i < length && isSuccess do
                                let! completer = Async.StartChild(Seq.item i steps, timeoutForStep)
                                let! result = completer
                                i <- i + 1
                                isSuccess <- isSuccess && result
                        }

                Async.RunSynchronously(ts, cancellationToken = linkedCTS.Token)

            with ex ->
                isSuccess <- false
                if linkedCTS.Token.IsCancellationRequested then
                    AnsiConsole.MarkupLine $"[yellow]Stage is cancelled or timeouted.[/]"
                    AnsiConsole.WriteLine()
                else
                    AnsiConsole.MarkupLine $"[red]Stage's step is failed: {ex.Message}[/]"
                    AnsiConsole.WriteException ex
                    AnsiConsole.WriteLine()

            AnsiConsole.Write(
                let color = if isSuccess then "teal" else "red"
                match index with
                | StageIndex.Stage i -> Rule($"""STAGE #{i} [bold {color}]{namePath}[/] finished. {stageSW.ElapsedMilliseconds}ms.""").LeftAligned()
                | StageIndex.Step i ->
                    Rule($"""SUBSTAGE [bold {color}]{stage.BuildStepPrefix i}[/] finished. {stageSW.ElapsedMilliseconds}ms.""")
                        .LeftAligned()
            )
            AnsiConsole.WriteLine()

        else
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine(
                match index with
                | StageIndex.Stage i -> $"STAGE #{i} [bold turquoise4]{namePath}[/] is inactive"
                | StageIndex.Step i -> $"SUBSTAGE [bold turquoise4]{stage.BuildStepPrefix i}[/] is inactive"
            )
            AnsiConsole.WriteLine()

        isSuccess, stepExns
