namespace rec Fun.Build

open System
open System.Diagnostics
open Spectre.Console
open Fun.Build
open Fun.Build.Internal


module StageContextExtensionsInternal =

    type StageContext with

        static member Create(name: string) = {
            Id = Random().Next()
            Name = name
            IsActive = fun _ -> true
            IsParallel = false
            Timeout = ValueNone
            TimeoutForStep = ValueNone
            WorkingDir = ValueNone
            EnvVars = Map.empty
            AcceptableExitCodes = set [| 0 |]
            FailIfIgnored = false
            NoPrefixForStep = true
            NoStdRedirectForStep = false
            ShuffleExecuteSequence = false
            ParentContext = ValueNone
            Steps = []
        }


        member ctx.GetMode() =
            match ctx.ParentContext with
            | ValueNone -> Mode.Execution
            | ValueSome(StageParent.Stage s) -> s.GetMode()
            | ValueSome(StageParent.Pipeline p) -> p.Mode


        member ctx.GetNamePath() =
            ctx.ParentContext
            |> ValueOption.map (
                function
                | StageParent.Stage x -> x.GetNamePath() + "/"
                | StageParent.Pipeline _ -> ""
            )
            |> ValueOption.defaultValue ""
            |> fun x -> x + ctx.Name


        member ctx.GetNoPrefixForStep() =
            match ctx.ParentContext with
            | ValueNone -> ctx.NoPrefixForStep
            | _ when not ctx.NoPrefixForStep -> ctx.NoPrefixForStep
            | ValueSome(StageParent.Stage s) -> s.GetNoPrefixForStep()
            | ValueSome(StageParent.Pipeline p) -> p.NoPrefixForStep


        member ctx.GetNoStdRedirectForStep() =
            match ctx.ParentContext with
            | ValueNone -> ctx.NoStdRedirectForStep
            | _ when ctx.NoStdRedirectForStep -> true
            | ValueSome(StageParent.Stage s) -> s.GetNoStdRedirectForStep()
            | ValueSome(StageParent.Pipeline p) -> p.NoStdRedirectForStep


        member ctx.BuildEnvVars() =
            let vars = Collections.Generic.Dictionary()

            ctx.ParentContext
            |> ValueOption.map (
                function
                | StageParent.Stage x -> x.BuildEnvVars()
                | StageParent.Pipeline x -> x.EnvVars
            )
            |> ValueOption.iter (fun kvs ->
                for KeyValue(k, v) in kvs do
                    vars[k] <- v
            )

            for KeyValue(k, v) in ctx.EnvVars do
                vars[k] <- v

            vars |> Seq.map (fun (KeyValue(k, v)) -> k, v) |> Map.ofSeq


        /// Will try to build the full path with nested sub stage index
        member ctx.BuildCurrentStepPrefix() =
            let mutable isSubStage = false
            let prefix =
                match ctx.ParentContext with
                | ValueNone -> ""
                | ValueSome(StageParent.Pipeline _) -> ""
                | ValueSome(StageParent.Stage parentStage) ->
                    let postfix =
                        parentStage.Steps
                        |> List.tryFindIndex (
                            function
                            | Step.StepOfStage x -> x.Id = ctx.Id
                            | _ -> false
                        )
                        |> Option.defaultValue 0
                        |> string
                    isSubStage <- true
                    sprintf "%s/step-%s" (parentStage.BuildCurrentStepPrefix()) postfix

            if String.IsNullOrEmpty prefix then ctx.Name
            else if isSubStage then sprintf "%s/%s" prefix ctx.Name
            else sprintf "%s/%s" prefix ctx.Name


        member inline ctx.BuildStepPrefix(i: int) = sprintf "%s/step-%s>" (ctx.BuildCurrentStepPrefix()) (string i)


        member ctx.BuildIndent(?margin) = String(' ', ctx.GetNamePath().Length - ctx.Name.Length + defaultArg margin 4)


        /// Verify if the exit code is allowed.
        member stage.IsAcceptableExitCode(exitCode: int) : bool =
            let parentAcceptableExitCodes =
                match stage.ParentContext with
                | ValueNone -> Set.empty
                | ValueSome(StageParent.Pipeline pipeline) -> pipeline.AcceptableExitCodes
                | ValueSome(StageParent.Stage parentStage) -> parentStage.AcceptableExitCodes

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

            if not isActive && stage.FailIfIgnored then
                let msg = $"Stage ({stage.GetNamePath()}) cannot be ignored (inactive)"
                AnsiConsole.MarkupLineInterpolated $"[red]{msg}[/]"
                let verifyStage =
                    { stage with
                        ParentContext =
                            match stage.ParentContext with
                            | ValueSome(StageParent.Pipeline p) -> ValueSome(StageParent.Pipeline { p with Mode = Mode.Verification })
                            | x -> x
                    }
                stage.IsActive(verifyStage) |> ignore
                raise (PipelineFailedException msg)

            else if isActive then
                let stageSW = Stopwatch.StartNew()
                let isParallel = stage.IsParallel
                let timeoutForStep: int = stage.GetTimeoutForStep()
                let timeoutForStage: int = stage.GetTimeoutForStage()

                let mutable isStageSoftCancelled = false

                use cts = new Threading.CancellationTokenSource(timeoutForStage)
                use stepErrorCTS = new Threading.CancellationTokenSource()
                use linkedStepErrorCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stepErrorCTS.Token)
                use linkedCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(linkedStepErrorCTS.Token, cancelToken)

                use stepCTS = new Threading.CancellationTokenSource(timeoutForStep)
                use linkedStepCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(stepCTS.Token, linkedCTS.Token)

                AnsiConsole.WriteLine()

                let extraInfo = $"timeout: {timeoutForStage}ms. step timeout: {timeoutForStep}ms."
                match index with
                | StageIndex.Stage i ->
                    AnsiConsole.Write(Rule($"[grey50]STAGE #{i} [bold turquoise4]{namePath}[/] started. {extraInfo}[/]").LeftJustified())
                | StageIndex.Step _ ->
                    AnsiConsole.MarkupLineInterpolated($"[grey50]{stage.BuildCurrentStepPrefix()}> sub-stage started. {extraInfo}[/]")

                let stage =
                    if stage.ShuffleExecuteSequence && stage.GetMode() = Mode.Execution then
                        { stage with
                            Steps = stage.Steps |> Seq.shuffle |> Seq.toList
                        }
                    else
                        stage

                let steps =
                    stage.Steps
                    |> Seq.mapi (fun i step -> async {
                        let prefix = stage.BuildStepPrefix(i)
                        try
                            let sw = Stopwatch.StartNew()
                            AnsiConsole.WriteLine()
                            AnsiConsole.MarkupLineInterpolated $"""[grey50]{prefix} started{if isParallel then " in parallel -->" else ""}[/]"""

                            let! isSuccess =
                                match step with
                                | Step.StepFn fn -> async {
                                    match! fn (stage, i) with
                                    | Error e ->
                                        if String.IsNullOrEmpty e |> not then
                                            if stage.GetNoPrefixForStep() then
                                                AnsiConsole.MarkupLineInterpolated $"""[red]{e}[/]"""
                                            else
                                                AnsiConsole.MarkupLineInterpolated $"""{prefix} error: [red]{e}[/]"""
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

                            AnsiConsole.MarkupLineInterpolated(
                                $"""[grey50]{prefix} finished{if isParallel then " in parallel." else "."} {sw.ElapsedMilliseconds}ms.[/]"""
                            )
                            if i = stage.Steps.Length - 1 then AnsiConsole.WriteLine()

                            if not isSuccess then stepErrorCTS.Cancel()
                            return isSuccess

                        with
                        | :? StepSoftCancelledException as ex ->
                            AnsiConsole.MarkupLineInterpolated $"[yellow]{prefix} {ex.Message}.[/]"
                            return true
                        | :? StageSoftCancelledException as ex ->
                            AnsiConsole.MarkupLineInterpolated $"[yellow]{prefix} {ex.Message}.[/]"
                            isStageSoftCancelled <- true
                            stepErrorCTS.Cancel()
                            return true
                        | ex ->
                            AnsiConsole.MarkupLineInterpolated $"[red]{prefix} exception hanppened.[/]"
                            AnsiConsole.WriteException ex
                            stepExns.Add ex
                            stepErrorCTS.Cancel()
                            return false
                    })

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

                with
                | _ when isStageSoftCancelled -> isSuccess <- true
                | ex ->
                    isSuccess <- false
                    if linkedCTS.Token.IsCancellationRequested then
                        AnsiConsole.MarkupLine $"[yellow]Stage is cancelled or timeouted.[/]"
                        AnsiConsole.WriteLine()
                    else
                        AnsiConsole.MarkupLine $"[red]Stage's step is failed[/]"
                        AnsiConsole.WriteException ex
                        AnsiConsole.WriteLine()


                let color = if isSuccess then "turquoise4" else "red"
                match index with
                | StageIndex.Stage i ->
                    AnsiConsole.Write(
                        Rule($"""[grey50]STAGE #{i} [bold {color}]{namePath}[/] finished. {stageSW.ElapsedMilliseconds}ms.[/]""")
                            .LeftJustified()
                    )
                | StageIndex.Step _ ->
                    AnsiConsole.MarkupLineInterpolated(
                        $"""[grey50]{stage.BuildCurrentStepPrefix()}> sub-stage finished. {stageSW.ElapsedMilliseconds}ms.[/]"""
                    )

                AnsiConsole.WriteLine()

            else
                AnsiConsole.WriteLine()

                match index with
                | StageIndex.Stage i -> AnsiConsole.Write(Rule($"[grey50]STAGE #{i} {namePath} is [yellow]in-active[/][/]").LeftJustified())
                | StageIndex.Step _ ->
                    AnsiConsole.MarkupLineInterpolated($"[grey50]{stage.BuildCurrentStepPrefix()}> sub-stage is [yellow]in-active[/][/]")

                AnsiConsole.WriteLine()

            isSuccess, stepExns


    let inline buildStageIsActive ([<InlineIfLambda>] build: BuildStage) ([<InlineIfLambda>] conditionFn) =
        BuildStage(fun ctx ->
            let newCtx = build.Invoke ctx
            { newCtx with
                IsActive =
                    fun ctx ->
                        match ctx.GetMode() with
                        | Mode.Execution -> newCtx.IsActive ctx && conditionFn ctx
                        | Mode.Verification
                        | Mode.CommandHelp _ ->
                            newCtx.IsActive ctx |> ignore
                            conditionFn ctx |> ignore
                            false
            }
        )


[<AutoOpen>]
module StageContextExtensions =

    type StageContext with

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

        member ctx.TryGetCmdArg(arg: CmdArg) =
            arg.Name.Names
            |> Seq.tryPick (fun x ->
                match ctx.TryGetCmdArg x with
                | ValueSome x -> Some x
                | ValueNone -> None
            )

        member inline ctx.GetCmdArg(key: string) = ctx.TryGetCmdArg key |> ValueOption.defaultValue ""
        member inline ctx.GetCmdArg(arg: CmdArg) = ctx.TryGetCmdArg arg |> Option.defaultValue ""


        member inline ctx.TryGetCmdArgOrEnvVar(key: string) =
            match ctx.TryGetCmdArg(key) with
            | ValueSome x -> ValueSome x
            | _ -> ctx.TryGetEnvVar(key)

        member inline ctx.GetCmdArgOrEnvVar(key) = ctx.TryGetCmdArgOrEnvVar key |> ValueOption.defaultValue ""


        /// It will cancel current step but mark it as successful
        member _.SoftCancelStep() = raise (StepSoftCancelledException "Step is soft cancelled")

        /// It will cancel current stage but mark it as successful
        member _.SoftCancelStage() = raise (StageSoftCancelledException "Stage is soft cancelled")
