[<AutoOpen>]
module Fun.Build.ConditionsBuilder

open System.Diagnostics
open System.Runtime.InteropServices
open Spectre.Console


type StageContext with

    member ctx.WhenEnvArg(envKey: string, envValue: string, description) =
        match ctx.Mode with
        | Mode.CommandHelp true ->
            printCommandOption (ctx.BuildIndent() + "env: ") (envKey + "  " + envValue) (defaultArg description "")
            false

        | Mode.CommandHelp false -> true

        | Mode.Execution ->
            match ctx.TryGetEnvVar envKey with
            | ValueSome v when envValue = "" || v = envValue -> true
            | _ -> false


    member ctx.WhenCmdArg(argKey: string, argValue: string, description) =
        match ctx.Mode with
        | Mode.CommandHelp verbose ->
            if verbose then
                printCommandOption (ctx.BuildIndent() + "cmd: ") (argKey + "  " + argValue) (defaultArg description "")
            else
                printCommandOption "  " (argKey + "  " + argValue) (defaultArg description "")

            false

        | Mode.Execution ->
            match ctx.TryGetCmdArg argKey with
            | ValueSome v when argValue = "" || v = argValue -> true
            | _ -> false


    member ctx.WhenBranch(branch: string) =
        match ctx.Mode with
        | Mode.CommandHelp verbose ->
            if verbose then
                AnsiConsole.MarkupLine $"{ctx.BuildIndent()}when branch is [green]{branch}[/]"
            false

        | Mode.Execution ->
            try
                let command = ctx.BuildCommand("git branch --show-current")
                ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command.WorkingDirectory <- x)

                let result = Process.Start command
                result.WaitForExit()
                result.StandardOutput.ReadLine() = branch
            with ex ->
                AnsiConsole.MarkupLine $"[red]Run git to get branch info failed: {ex.Message}[/]"
                false


    member ctx.WhenPlatform(platform: OSPlatform) =
        match ctx.Mode with
        | Mode.CommandHelp true ->
            AnsiConsole.MarkupLine $"{ctx.BuildIndent()}when platform is [green]{platform}[/]"
            false

        | Mode.CommandHelp false -> true

        | Mode.Execution -> RuntimeInformation.IsOSPlatform platform


type ConditionsBuilder() =

    member inline _.Yield(_: unit) = BuildConditions(fun x -> x)

    member inline _.Yield(x: BuildStageIsActive) = x

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildConditions) = BuildConditions(fun conds -> fn().Invoke(conds))
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) = BuildConditions(fun conds -> conds @ [ fn().Invoke ])


    member inline _.Combine([<InlineIfLambda>] buildStageIsActive: BuildStageIsActive, [<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions -> builder.Invoke(conditions) @ [ buildStageIsActive.Invoke ])


    member inline _.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildConditions) =
        BuildConditions(fun conds -> fn().Invoke(builder.Invoke(conds)))

    member inline _.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        BuildConditions(fun conds -> builder.Invoke(conds) @ [ fn().Invoke ])


    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, ?envValue: string, ?description: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenEnvArg(envKey, defaultArg envValue "", description)
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKey: string, ?argValue: string, ?description: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(argKey, defaultArg argValue "", description)
            ]
        )

    [<CustomOperation("branch")>]
    member inline _.branch([<InlineIfLambda>] builder: BuildConditions, branch: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenBranch(branch)
            ]
        )

    [<CustomOperation("platformWindows")>]
    member inline _.platformWindows([<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenPlatform OSPlatform.Windows
            ]
        )

    [<CustomOperation("platformLinux")>]
    member inline _.platformLinux([<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenPlatform OSPlatform.Linux
            ]
        )

    [<CustomOperation("platformOSX")>]
    member inline _.platformOSX([<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenPlatform OSPlatform.OSX
            ]
        )


type StageBuilder with

    /// Set if stage is active or should run.
    /// Only the last condition will take effect.
    [<CustomOperation("when'")>]
    member inline _.when'([<InlineIfLambda>] build: BuildStage, value: bool) =
        BuildStage(fun ctx -> { build.Invoke ctx with IsActive = fun _ -> value })


    /// Set if stage is active or should run by check the environment variable.
    /// Only the last condition will take effect.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, ?envValue: string, ?description) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenEnvArg(envKey, defaultArg envValue "", description)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKey: string, ?argValue: string, ?description) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(argKey, defaultArg argValue "", description)
            }
        )

    /// Set if stage is active or should run by check the git branch name.
    /// Only the last condition will take effect.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch([<InlineIfLambda>] build: BuildStage, branch: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenBranch branch
            }
        )

    /// Set if stage is active or should run by check the platform is Windows.
    /// Only the last condition will take effect.
    [<CustomOperation("whenWindows")>]
    member inline _.whenWindows([<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenPlatform OSPlatform.Windows
            }
        )

    /// Set if stage is active or should run by check the platform is Linux.
    /// Only the last condition will take effect.
    [<CustomOperation("whenLinux")>]
    member inline _.whenLinux([<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenPlatform OSPlatform.Linux
            }
        )

    /// Set if stage is active or should run by check the platform is OSX.
    /// Only the last condition will take effect.
    [<CustomOperation("whenOSX")>]
    member inline _.whenOSX([<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenPlatform OSPlatform.OSX
            }
        )


type PipelineBuilder with

    /// Set if stage is active or should run.
    /// Only the last condition will take effect.
    [<CustomOperation("when'")>]
    member inline _.when'([<InlineIfLambda>] build: BuildPipeline, value: bool) =
        BuildPipeline(fun ctx -> { build.Invoke ctx with Verify = fun _ -> value })


    /// Set if stage is active or should run by check the environment variable.
    /// Only the last condition will take effect.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string, ?envValue: string, ?description) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenEnvArg(envKey, defaultArg envValue "", description)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKey: string, ?argValue: string, ?description) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(argKey, defaultArg argValue "", description)
            }
        )

    /// Set if stage is active or should run by check the git branch name.
    /// Only the last condition will take effect.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch([<InlineIfLambda>] build: BuildPipeline, branch: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenBranch branch
            }
        )

    /// Set if stage is active or should run by check the platform is Windows.
    /// Only the last condition will take effect.
    [<CustomOperation("whenWindows")>]
    member inline _.whenWindows([<InlineIfLambda>] build: BuildPipeline) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenPlatform OSPlatform.Windows
            }
        )

    /// Set if stage is active or should run by check the platform is Linux.
    /// Only the last condition will take effect.
    [<CustomOperation("whenLinux")>]
    member inline _.whenLinux([<InlineIfLambda>] build: BuildPipeline) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenPlatform OSPlatform.Linux
            }
        )

    /// Set if stage is active or should run by check the platform is OSX.
    /// Only the last condition will take effect.
    [<CustomOperation("whenOSX")>]
    member inline _.whenOSX([<InlineIfLambda>] build: BuildPipeline) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenPlatform OSPlatform.OSX
            }
        )


type WhenAnyBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.Mode with
            | Mode.CommandHelp verbose ->
                if verbose then
                    AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when any below conditions are met[/]"
                let indentCtx =
                    { StageContext.Create "  " with
                        ParentContext = ctx.ParentContext
                    }
                let newCtx =
                    { ctx with
                        ParentContext = ValueSome(StageParent.Stage indentCtx)
                    }
                builder.Invoke [] |> Seq.iter (fun fn -> fn newCtx |> ignore)
                false

            | Mode.Execution -> builder.Invoke [] |> Seq.exists (fun fn -> fn ctx)
        )


type WhenAllBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.Mode with
            | Mode.CommandHelp verbose ->
                if verbose then
                    AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when all below conditions are met[/]"
                let indentCtx =
                    { StageContext.Create "  " with
                        ParentContext = ctx.ParentContext
                    }
                let newCtx =
                    { ctx with
                        ParentContext = ValueSome(StageParent.Stage indentCtx)
                    }
                builder.Invoke [] |> Seq.iter (fun fn -> fn newCtx |> ignore)
                false

            | Mode.Execution -> builder.Invoke [] |> Seq.map (fun fn -> fn ctx) |> Seq.reduce (fun x y -> x && y)
        )


type WhenNotBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.Mode with
            | Mode.CommandHelp true ->
                AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when all below conditions are [bold red]NOT[/] met[/]"
                let indentCtx =
                    { StageContext.Create "  " with
                        ParentContext = ctx.ParentContext
                    }
                let newCtx =
                    { ctx with
                        ParentContext = ValueSome(StageParent.Stage indentCtx)
                    }
                builder.Invoke [] |> Seq.iter (fun fn -> fn newCtx |> ignore)
                false

            | Mode.CommandHelp false -> false

            | Mode.Execution -> builder.Invoke [] |> Seq.map (fun fn -> not (fn ctx)) |> Seq.reduce (fun x y -> x && y)
        )


/// When any of the added conditions are satisified, the stage will be active
let whenAny = WhenAnyBuilder()
/// When all of the added conditions are satisified, the stage will be active
let whenAll = WhenAllBuilder()
/// When all of the added conditions are not satisified, the stage will be active
let whenNot = WhenNotBuilder()
