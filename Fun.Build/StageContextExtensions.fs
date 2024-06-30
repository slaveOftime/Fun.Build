namespace rec Fun.Build

open System
open System.Threading
open System.Threading.Tasks
open System.Net.Http
open System.Diagnostics
open Spectre.Console
open Fun.Result
open Fun.Build
open Fun.Build.Internal


module StageContextExtensionsInternal =

    type StageContext with

        static member Create(name: string) = {
            Id = Random().Next()
            Name = name
            IsActive = fun _ -> true
            IsParallel = fun _ -> false
            ContinueStepsOnFailure = false
            ContinueStageOnFailure = false
            Timeout = ValueNone
            TimeoutForStep = ValueNone
            WorkingDir = ValueNone
            EnvVars = Map.empty
            AcceptableExitCodes = set [| 0 |]
            FailIfIgnored = false
            FailIfNoActiveSubStage = false
            NoPrefixForStep = true
            NoStdRedirectForStep = false
            ShuffleExecuteSequence = false
            ParentContext = ValueNone
            Steps = []
        }


        member ctx.GetParentPipeline() =
            match ctx.ParentContext with
            | ValueNone -> None
            | ValueSome(StageParent.Stage s) -> s.GetParentPipeline()
            | ValueSome(StageParent.Pipeline p) -> Some p


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
        member stage.Run(index: StageIndex, cancellationToken: Threading.CancellationToken) =
            let mutable isSuccess = true
            let stepExns = ResizeArray<exn>()

            let isActive = stage.IsActive stage
            let namePath = stage.GetNamePath()
            let pipeline = stage.GetParentPipeline()

            pipeline |> Option.iter (fun x -> x.RunBeforeEachStage stage)

            try
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
                    if stage.FailIfNoActiveSubStage then
                        let parentContext = ValueSome(StageParent.Stage stage)
                        let hasActiveStep =
                            stage.Steps
                            |> Seq.exists (
                                function
                                | Step.StepOfStage s -> s.IsActive { s with ParentContext = parentContext }
                                | _ -> false
                            )
                        if not hasActiveStep then
                            AnsiConsole.MarkupLineInterpolated
                                $"[red]Pipeline is failed because there is no active sub stages but stage ({stage.GetNamePath()}) requires at least one[/]"
                            raise (PipelineFailedException "No active sub stages")

                    let stageSW = Stopwatch.StartNew()
                    let isParallel = stage.IsParallel stage
                    let timeoutForStep: int = stage.GetTimeoutForStep()
                    let timeoutForStage: int = stage.GetTimeoutForStage()

                    let mutable isStageSoftCancelled = false

                    use cts = new Threading.CancellationTokenSource(timeoutForStage)
                    use stepErrorCTS = new Threading.CancellationTokenSource()
                    use linkedStepErrorCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stepErrorCTS.Token)
                    use linkedCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(linkedStepErrorCTS.Token, cancellationToken)

                    use stepCTS = new Threading.CancellationTokenSource(timeoutForStep)
                    use linkedStepCTS = Threading.CancellationTokenSource.CreateLinkedTokenSource(stepCTS.Token, linkedCTS.Token)

                    AnsiConsole.WriteLine()

                    let extraInfo = $"timeout: {timeoutForStage}ms. step timeout: {timeoutForStep}ms."
                    match index with
                    | StageIndex.Stage i ->
                        AnsiConsole.Write(Rule($"[grey50]STAGE #{i} [bold turquoise4]{namePath}[/] started. {extraInfo}[/]").LeftJustified())
                    | StageIndex.Step _ ->
                        AnsiConsole.MarkupLineInterpolated($"[grey50]{stage.BuildCurrentStepPrefix()}> sub-stage started. {extraInfo}[/]")

                    let indexedSteps =
                        stage.Steps
                        |> Seq.mapi (fun i s -> i, s)
                        |> (if stage.ShuffleExecuteSequence && stage.GetMode() = Mode.Execution then
                                Seq.shuffle
                            else
                                id)

                    let steps =
                        indexedSteps
                        |> Seq.map (fun (i, step) -> async {
                            let prefix = stage.BuildStepPrefix(i)
                            let exns = ResizeArray<Exception>()
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
                                                if not isParallel && stage.GetNoPrefixForStep() then
                                                    AnsiConsole.MarkupLineInterpolated $"""[red]Error: {e}[/]"""
                                                else
                                                    AnsiConsole.MarkupLineInterpolated $"""[red]Error: {prefix} {e}[/]"""
                                            return false
                                        | Ok _ -> return true
                                      }
                                    | Step.StepOfStage subStage -> async {
                                        let subStage =
                                            { subStage with
                                                ParentContext = ValueSome(StageParent.Stage stage)
                                            }
                                        let isSuccess, es = subStage.Run(StageIndex.Step i, linkedStepCTS.Token)
                                        exns.AddRange es
                                        return isSuccess
                                      }

                                let color = if isSuccess then "grey50" else "red"

                                AnsiConsole.MarkupLineInterpolated(
                                    $"""[{color}]{prefix} finished{if isParallel then " in parallel." else "."} {sw.ElapsedMilliseconds}ms.[/]"""
                                )
                                if i = stage.Steps.Length - 1 then AnsiConsole.WriteLine()

                                return isSuccess, exns

                            with
                            | :? StepSoftCancelledException as ex ->
                                AnsiConsole.MarkupLineInterpolated $"[yellow]{prefix} {ex.Message}.[/]"
                                return true, exns
                            | :? StageSoftCancelledException as ex ->
                                AnsiConsole.MarkupLineInterpolated $"[yellow]{prefix} {ex.Message}.[/]"
                                isStageSoftCancelled <- true
                                stepErrorCTS.Cancel()
                                return true, exns
                            | ex ->
                                AnsiConsole.MarkupLineInterpolated $"[red]{prefix} exception happened.[/]"
                                AnsiConsole.WriteException ex
                                if not stage.ContinueStageOnFailure then
                                    exns.Add(Exception(prefix + " " + ex.Message, ex.InnerException))
                                return false, exns
                        })

                    try
                        let handleExn (exns: ResizeArray<Exception>) =
                            if exns.Count > 0 then
                                if not stage.ContinueStageOnFailure then stepExns.AddRange exns
                                if not stage.ContinueStepsOnFailure then stepErrorCTS.Cancel()

                        let ts =
                            if isParallel then
                                async {
                                    let completers = ResizeArray()

                                    for ts in steps do
                                        let! completer = Async.StartChild(ts, timeoutForStep)
                                        completers.Add completer

                                    let mutable i = 0
                                    while i < completers.Count && (stage.ContinueStepsOnFailure || isSuccess) do
                                        let! result, exns = completers[i]
                                        handleExn exns
                                        if not result && not stage.ContinueStepsOnFailure then stepErrorCTS.Cancel()
                                        i <- i + 1
                                        isSuccess <- isSuccess && result
                                }
                            else
                                async {
                                    let mutable i = 0
                                    let length = Seq.length steps
                                    while i < length && (stage.ContinueStepsOnFailure || isSuccess) do
                                        let! completer = Async.StartChild(Seq.item i steps, timeoutForStep)
                                        let! result, exns = completer
                                        handleExn exns
                                        i <- i + 1
                                        isSuccess <- isSuccess && result
                                }

                        Async.RunSynchronously(ts, cancellationToken = linkedCTS.Token)

                    with
                    | _ when isStageSoftCancelled -> isSuccess <- true
                    | ex ->
                        isSuccess <- false
                        if linkedCTS.Token.IsCancellationRequested && not stepErrorCTS.IsCancellationRequested then
                            AnsiConsole.MarkupLine $"[yellow]Stage is cancelled or timeouted.[/]"
                            AnsiConsole.WriteLine()
                        else if not stepErrorCTS.IsCancellationRequested then
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
                    | StageIndex.Stage i -> AnsiConsole.Write(Rule($"[grey50]STAGE #{i} {namePath} is [yellow]inactive[/][/]").LeftJustified())
                    | StageIndex.Step _ ->
                        AnsiConsole.MarkupLineInterpolated($"[grey50]{stage.BuildCurrentStepPrefix()}> sub-stage is [yellow]inactive[/][/]")

                    AnsiConsole.WriteLine()

            finally
                pipeline |> Option.iter (fun x -> x.RunAfterEachStage stage)

            stage.ContinueStageOnFailure || isSuccess, stepExns


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

        /// Stage under pipeline should be level 0, the level will get increased for nested stages
        member ctx.GetStageLevel() =
            match ctx.ParentContext with
            | ValueNone -> 0
            | ValueSome(StageParent.Stage x) -> x.GetStageLevel() + 1
            | ValueSome(StageParent.Pipeline _) -> 0

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


        member ctx.GetAllEnvVars() =
            match ctx.ParentContext with
            | ValueSome(StageParent.Pipeline p) -> p.EnvVars
            | ValueSome(StageParent.Stage s) -> s.GetAllEnvVars()
            | ValueNone -> Map.empty
            |> fun envVars -> Map.fold (fun s k v -> Map.add k v s) envVars ctx.EnvVars

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

        member ctx.GetAllCmdArgs() =
            match ctx.ParentContext with
            | ValueSome(StageParent.Pipeline p) -> p.CmdArgs
            | ValueSome(StageParent.Stage s) -> s.GetAllCmdArgs()
            | ValueNone -> []

        member ctx.TryGetCmdArg(key: string) =
            let cmdArgs = ctx.GetAllCmdArgs()
            match cmdArgs |> List.tryFindIndex ((=) key) with
            | Some index ->
                if List.length cmdArgs > index + 1 then
                    ValueSome cmdArgs[index + 1]
                else
                    ValueSome ""
            | _ -> ValueNone

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


        member _.RunHttpHealthCheck(url: string, ?configRequest: HttpRequestMessage -> unit, ?cancellationToken: CancellationToken) = asyncResult {
            use client = new HttpClient()
            let ct = defaultArg cancellationToken CancellationToken.None

            let mutable shouldContinue = true
            while shouldContinue && not ct.IsCancellationRequested do
                try
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Check {url} ...[/]")

                    use message = new HttpRequestMessage(HttpMethod.Get, url)

                    configRequest |> Option.iter (fun fn -> fn message)

                    let! result = client.SendAsync(message, cancellationToken = ct) |> AsyncResult.ofTask
                    shouldContinue <- not result.IsSuccessStatusCode

                with
                | :? TaskCanceledException when ct.IsCancellationRequested -> shouldContinue <- false
                | ex -> AnsiConsole.MarkupLineInterpolated($"[red]Health check failed: {ex.Message}[/]")

                do! Async.Sleep 1000 |> Async.map Ok

            if ct.IsCancellationRequested then
                do! AsyncResult.ofError "Health check is cancelled"
            else
                AnsiConsole.MarkupLineInterpolated($"[green]{url} is healthy![/]")
        }
