[<AutoOpen>]
module Fun.Build.ConditionsBuilder

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Spectre.Console
open Fun.Build.Internal
open Fun.Build.BuiltinCmdsInternal
open Fun.Build.StageContextExtensions
open Fun.Build.StageContextExtensionsInternal
open Fun.Build.PipelineContextExtensionsInternal


module Internal =

    type StageContext with

        member ctx.WhenEnvArg(envKey: string, envValue: string, description) =
            let getResult () =
                match ctx.TryGetEnvVar envKey with
                | ValueSome v when envValue = "" || v = envValue -> true
                | _ -> false

            let getPrintInfo (prefix: string) =
                makeCommandOption
                    (prefix + "env: ")
                    (envKey + if String.IsNullOrEmpty envValue then "" else " = " + envValue)
                    (defaultArg description "")

            match ctx.GetMode() with
            | Mode.CommandHelp true ->
                AnsiConsole.WriteLine(getPrintInfo (ctx.BuildIndent()))
                false
            | Mode.CommandHelp false -> true
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ {getPrintInfo (ctx.BuildIndent().Substring(2))}[/]")
                else
                    AnsiConsole.MarkupLineInterpolated($"[red]✕ {getPrintInfo (ctx.BuildIndent().Substring(2))}[/]")
                false
            | Mode.Execution -> getResult ()


        member ctx.WhenCmd(info: CmdInfo) =
            let getResult () =
                let isValueMatch v =
                    match ctx.TryGetCmdArg v with
                    | ValueSome v when info.Values.Length = 0 || List.contains v info.Values -> true
                    | _ -> false
                isValueMatch info.Name
                || (
                    match info.Alias with
                    | None -> false
                    | Some alias -> isValueMatch alias
                )

            let makeNameForPrint () = info.Name + (info.Alias |> Option.map (sprintf ", %s") |> Option.defaultValue "")

            let makeValuesForPrint () =
                match info.Values with
                | [] -> ""
                | _ -> Environment.NewLine + "[[choices: " + String.concat ", " (info.Values |> Seq.map (sprintf "\"%s\"")) + "]]"


            let getPrintInfo (prefix: string) =
                makeCommandOption (prefix + "cmd: ") (makeNameForPrint ()) (defaultArg info.Description "" + makeValuesForPrint ())

            match ctx.GetMode() with
            | Mode.CommandHelp true ->
                AnsiConsole.WriteLine(getPrintInfo (ctx.BuildIndent()))
                false
            | Mode.CommandHelp false ->
                AnsiConsole.WriteLine(makeCommandOption "  " (makeNameForPrint ()) (defaultArg info.Description "" + makeValuesForPrint ()))
                false
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated $"""[green]{getPrintInfo ("✓ " + ctx.BuildIndent().Substring(2))}[/]"""
                else
                    AnsiConsole.MarkupLineInterpolated $"""[red]{getPrintInfo ("✕ " + ctx.BuildIndent().Substring(2))}[/]"""
                false
            | Mode.Execution -> getResult ()


        member ctx.WhenCmdArg(argKey: string, argValue: string, description) =
            ctx.WhenCmd
                {
                    Name = argKey
                    Alias = None
                    Description = description
                    Values = [
                        if String.IsNullOrEmpty argValue |> not then argValue
                    ]
                }


        member ctx.WhenBranch(branch: string) =
            let getResult () =
                try
                    let command = ctx.BuildCommand("git branch --show-current")
                    ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command.WorkingDirectory <- x)

                    let result = Process.Start command
                    result.WaitForExit()
                    result.StandardOutput.ReadLine() = branch
                with ex ->
                    AnsiConsole.MarkupLineInterpolated $"[red]Run git to get branch info failed: {ex.Message}[/]"
                    false

            match ctx.GetMode() with
            | Mode.CommandHelp verbose ->
                if verbose then
                    AnsiConsole.MarkupLineInterpolated $"{ctx.BuildIndent()}when branch is [green]{branch}[/]"
                false
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated $"[green]✓ [/]{ctx.BuildIndent().Substring(2)}when branch is [green]{branch}[/]"
                else
                    AnsiConsole.MarkupLineInterpolated $"[red]✕ [/]{ctx.BuildIndent().Substring(2)}when branch is [red]{branch}[/]"
                false
            | Mode.Execution -> getResult ()


        member ctx.WhenPlatform(platform: OSPlatform) =
            let getResult () = RuntimeInformation.IsOSPlatform platform

            match ctx.GetMode() with
            | Mode.CommandHelp true ->
                AnsiConsole.MarkupLine $"{ctx.BuildIndent()}when platform is [green]{platform}[/]"
                false
            | Mode.CommandHelp false -> true
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLine $"[green]✓ [/]{ctx.BuildIndent().Substring(2)}when platform is [green]{platform}[/]"
                else
                    AnsiConsole.MarkupLine $"[red]✕ [/]{ctx.BuildIndent().Substring(2)}when platform is [red]{platform}[/]"
                false
            | Mode.Execution -> getResult ()


open Internal


type StageContext with

    member ctx.IsOSX = ctx.WhenPlatform(OSPlatform.OSX)
    member ctx.IsLinux = ctx.WhenPlatform(OSPlatform.Linux)
    member ctx.IsWindows = ctx.WhenPlatform(OSPlatform.Windows)

    member ctx.IsBranch(branch) = ctx.WhenBranch(branch)


type ConditionsBuilder() =

    member inline _.Yield(_: unit) = BuildConditions(fun x -> x)

    member inline _.Yield(x: BuildStageIsActive) = x

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildConditions) = BuildConditions(fun conds -> fn().Invoke(conds))
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) = BuildConditions(fun conds -> conds @ [ fn().Invoke ])


    member inline _.Combine([<InlineIfLambda>] buildStageIsActive: BuildStageIsActive, [<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions -> [ buildStageIsActive.Invoke ] @ builder.Invoke(conditions))


    member inline _.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildConditions) =
        BuildConditions(fun conds -> fn().Invoke(builder.Invoke(conds)))

    member inline this.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        this.Combine(fn (), builder)


    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenEnvArg(envKey, "", None)
            ]
        )

    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, envValue: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenEnvArg(envKey, envValue, None)
            ]
        )

    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, envValue: string, description: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenEnvArg(envKey, envValue, Some description)
            ]
        )


    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKey: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(argKey, "", None)
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKey: string, argValue: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(argKey, argValue, None)
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKey: string, argValue: string, description: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(argKey, argValue, Some description)
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
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenEnvArg(envKey, "", None)
            }
        )

    /// Set if stage is active or should run by check the environment variable.
    /// Only the last condition will take effect.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, envValue: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenEnvArg(envKey, envValue, None)
            }
        )

    /// Set if stage is active or should run by check the environment variable.
    /// Only the last condition will take effect.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, envValue: string, description: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenEnvArg(envKey, envValue, Some description)
            }
        )


    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKey: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(argKey, "", None)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKey: string, argValue: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(argKey, argValue, None)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKey: string, argValue: string, description: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(argKey, argValue, Some description)
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
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenEnvArg(envKey, "", None)
            }
        )

    /// Set if stage is active or should run by check the environment variable.
    /// Only the last condition will take effect.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string, envValue: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenEnvArg(envKey, envValue, None)
            }
        )

    /// Set if stage is active or should run by check the environment variable.
    /// Only the last condition will take effect.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string, envValue: string, description: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenEnvArg(envKey, envValue, Some description)
            }
        )


    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKey: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(argKey, "", None)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKey: string, argValue: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(argKey, argValue, None)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKey: string, argValue: string, description: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(argKey, argValue, Some description)
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
            match ctx.GetMode() with
            | Mode.Execution -> builder.Invoke [] |> Seq.exists (fun fn -> fn ctx)
            | _ ->
                match ctx.GetMode() with
                | Mode.Verification
                | Mode.CommandHelp true -> AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when any below conditions are met[/]"
                | _ -> ()

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
        )


type WhenAllBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.GetMode() with
            | Mode.Execution -> builder.Invoke [] |> Seq.map (fun fn -> fn ctx) |> Seq.reduce (fun x y -> x && y)
            | _ ->
                match ctx.GetMode() with
                | Mode.Verification
                | Mode.CommandHelp true -> AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when all below conditions are met[/]"
                | _ -> ()

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
        )


type WhenNotBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.GetMode() with
            | Mode.Verification
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


type WhenCmdBuilder() =

    member _.Run(build: BuildCmdInfo) =
        BuildStageIsActive(fun ctx ->
            let cmdInfo = build.Invoke { Name = ""; Alias = None; Description = None; Values = [] }
            ctx.WhenCmd(cmdInfo)
        )

    member inline _.Yield(_: unit) = BuildCmdInfo(fun x -> x)
    member inline _.Yield(x: BuildCmdInfo) = x
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildCmdInfo) = BuildCmdInfo(fun x -> fn().Invoke(x))

    [<CustomOperation "name">]
    member inline _.name([<InlineIfLambda>] build: BuildCmdInfo, x: string) = BuildCmdInfo(fun info -> { build.Invoke(info) with Name = x })

    [<CustomOperation "alias">]
    member inline _.alias([<InlineIfLambda>] build: BuildCmdInfo, x: string) = BuildCmdInfo(fun info -> { build.Invoke(info) with Alias = Some x })

    [<CustomOperation "description">]
    member inline _.description([<InlineIfLambda>] build: BuildCmdInfo, x: string) =
        BuildCmdInfo(fun info -> { build.Invoke(info) with Description = Some x })

    [<CustomOperation "value">]
    member inline _.value([<InlineIfLambda>] build: BuildCmdInfo, x: string) = BuildCmdInfo(fun info -> { build.Invoke(info) with Values = [ x ] })

    [<CustomOperation "acceptValues">]
    member inline _.acceptValues([<InlineIfLambda>] build: BuildCmdInfo, values: string list) =
        BuildCmdInfo(fun info -> { build.Invoke(info) with Values = values })


/// When any of the added conditions are satisified, the stage will be active
let whenAny = WhenAnyBuilder()
/// When all of the added conditions are satisified, the stage will be active
let whenAll = WhenAllBuilder()
/// When all of the added conditions are not satisified, the stage will be active
let whenNot = WhenNotBuilder()
/// When the cmd is matched, the stage will be active
let whenCmd = WhenCmdBuilder()
