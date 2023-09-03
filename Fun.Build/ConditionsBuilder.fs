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
            | Mode.CommandHelp { Verbose = true } ->
                AnsiConsole.WriteLine(getPrintInfo (ctx.BuildIndent()))
                false
            | Mode.CommandHelp _ -> true
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ {getPrintInfo (ctx.BuildIndent().Substring(2))}[/]")
                else
                    AnsiConsole.MarkupLineInterpolated($"[red]✕ {getPrintInfo (ctx.BuildIndent().Substring(2))}[/]")
                false
            | Mode.Execution -> getResult ()


        member ctx.WhenCmd(info: CmdArg) =
            let mode = ctx.GetMode()
            if info.Name.Names |> Seq.filter (String.IsNullOrEmpty >> not) |> Seq.isEmpty then
                failwith "Cmd name cannot be empty"

            let getResult () =
                let isValueMatch (v: string) =
                    match ctx.TryGetCmdArg v with
                    | ValueSome v when info.Values.Length = 0 || List.contains v info.Values -> true
                    | _ -> false

                if info.IsOptional then true else info.Name.Names |> Seq.exists isValueMatch

            let makeNameForPrint () =
                match mode with
                | Mode.CommandHelp { Verbose = false } ->
                    match info.Name with
                    | CmdName.ShortName x -> x
                    | CmdName.LongName x -> $"    {x}"
                    | CmdName.FullName(s, l) -> $"{s}, {l}"
                | _ ->
                    match info.Name with
                    | CmdName.ShortName x -> x
                    | CmdName.LongName x -> x
                    | CmdName.FullName(s, l) -> $"{s}, {l}"
                // If the command is optional, then wrap it with []
                |> fun x -> if info.IsOptional then $"[{x}]" else x

            let makeValuesForPrint () =
                match info.Values with
                | [] -> ""
                | _ -> Environment.NewLine + "[choices: " + String.concat ", " (info.Values |> Seq.map (sprintf "\"%s\"")) + "]"


            let getPrintInfo (prefix: string) =
                makeCommandOption (prefix + "cmd: ") (makeNameForPrint ()) (defaultArg info.Description "" + makeValuesForPrint ())

            match mode with
            | Mode.CommandHelp { Verbose = true } ->
                AnsiConsole.WriteLine(getPrintInfo (ctx.BuildIndent()))
                false
            | Mode.CommandHelp cmdHelpCtx ->
                let isCmdInfoAlreadyAdded =
                    cmdHelpCtx.CmdInfos
                    |> Seq.exists (fun addedCmdInfo -> addedCmdInfo.Name.Names |> Seq.exists (fun n -> Seq.contains n info.Name.Names))
                cmdHelpCtx.CmdInfos.Add info
                if isCmdInfoAlreadyAdded then
                    // No need to print duplicate cmd info when name is same in none verbose mode
                    false
                else
                    AnsiConsole.WriteLine(makeCommandOption "  " (makeNameForPrint ()) (defaultArg info.Description "" + makeValuesForPrint ()))
                    false
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated $"""[green]{getPrintInfo ("✓ " + ctx.BuildIndent().Substring(2))}[/]"""
                else
                    AnsiConsole.MarkupLineInterpolated $"""[red]{getPrintInfo ("✕ " + ctx.BuildIndent().Substring(2))}[/]"""
                false
            | Mode.Execution -> getResult ()


        member ctx.WhenCmdArg(name: CmdName, argValue: string, description, isOptional) =
            ctx.WhenCmd
                {
                    Name = name
                    Description = description
                    Values = [
                        if String.IsNullOrEmpty argValue |> not then argValue
                    ]
                    IsOptional = isOptional
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
            | Mode.CommandHelp cmdHelpCtx ->
                if cmdHelpCtx.Verbose then
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
            | Mode.CommandHelp { Verbose = true } ->
                AnsiConsole.MarkupLine $"{ctx.BuildIndent()}when platform is [green]{platform}[/]"
                false
            | Mode.CommandHelp _ -> true
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
        BuildConditions(fun conditions -> builder.Invoke(conditions @ [ buildStageIsActive.Invoke ]))


    member inline _.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildConditions) =
        BuildConditions(fun conds -> fn().Invoke(builder.Invoke(conds)))

    member inline this.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        BuildConditions(fun conditions -> builder.Invoke(conditions @ [ fn().Invoke ]))


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
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, arg: CmdArg) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmd arg
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKeyLongName: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, "", None, false)
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKeyLongName: string, argValue: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue, None, false)
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKeyLongName: string, argValue: string, description: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue, Some description, false)
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg
        (
            [<InlineIfLambda>] builder: BuildConditions,
            argKeyLongName: string,
            argValue: string,
            description: string,
            isOptional: bool
        ) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue, Some description, isOptional)
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
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, arg: CmdArg) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmd arg
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, "", None, false)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string, argValue: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue, None, false)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string, argValue: string, description: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue, Some description, false)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string, argValue: string, description: string, isOptional) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue, Some description, isOptional)
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
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, arg: CmdArg) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmd(arg)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKeyLongName: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(CmdName.LongName argKeyLongName, "", None, false)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKeyLongName: string, argValue: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(CmdName.LongName argKeyLongName, argValue, None, false)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKeyLongName: string, argValue: string, description: string) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(CmdName.LongName argKeyLongName, argValue, Some description, false)
            }
        )

    /// Set if stage is active or should run by check the command line args.
    /// Only the last condition will take effect.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg
        (
            [<InlineIfLambda>] build: BuildPipeline,
            argKeyLongName: string,
            argValue: string,
            description: string,
            isOptional: bool
        ) =
        BuildPipeline(fun ctx ->
            { build.Invoke ctx with
                Verify = fun ctx -> ctx.MakeVerificationStage().WhenCmdArg(CmdName.LongName argKeyLongName, argValue, Some description, isOptional)
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
                | Mode.CommandHelp { Verbose = true } -> AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when any below conditions are met[/]"
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
                | Mode.CommandHelp { Verbose = true } -> AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when all below conditions are met[/]"
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
            | Mode.CommandHelp { Verbose = true } ->
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

            | Mode.CommandHelp _ -> false

            | Mode.Execution -> builder.Invoke [] |> Seq.map (fun fn -> not (fn ctx)) |> Seq.reduce (fun x y -> x && y)
        )


type WhenCmdBuilder() =

    member _.Run(build: BuildCmdInfo) =
        BuildStageIsActive(fun ctx ->
            let cmdInfo =
                build.Invoke
                    {
                        // We should carefully procees the empty string in this build type
                        Name = CmdName.ShortName ""
                        Description = None
                        Values = []
                        IsOptional = false
                    }
            ctx.WhenCmd(cmdInfo)
        )

    member inline _.Yield(_: unit) = BuildCmdInfo(fun x -> x)
    member inline _.Yield(x: BuildCmdInfo) = x
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildCmdInfo) = BuildCmdInfo(fun x -> fn().Invoke(x))


    /// Short name, long name
    [<CustomOperation "fullName">]
    member inline _.fullName([<InlineIfLambda>] build: BuildCmdInfo, shortName: string, longName: string) =
        BuildCmdInfo(fun info ->
            { build.Invoke(info) with
                Name = CmdName.FullName(shortName, longName)
            }
        )

    /// It is the same as shortName
    [<CustomOperation "name">]
    member inline this.name([<InlineIfLambda>] build: BuildCmdInfo, x: string) = this.shortName (build, x)

    /// It is the same as longName
    [<CustomOperation "alias">]
    member inline this.alias([<InlineIfLambda>] build: BuildCmdInfo, x: string) = this.longName (build, x)


    [<CustomOperation "shortName">]
    member _.shortName(build: BuildCmdInfo, x: string) =
        BuildCmdInfo(fun info ->
            let info = build.Invoke(info)
            { info with
                Name =
                    match info.Name with
                    | CmdName.FullName(_, longName)
                    | CmdName.LongName longName when not (String.IsNullOrEmpty longName) -> CmdName.FullName(x, longName)
                    | _ -> CmdName.ShortName x
            }
        )

    [<CustomOperation "longName">]
    member _.longName(build: BuildCmdInfo, x: string) =
        BuildCmdInfo(fun info ->
            let info = build.Invoke(info)
            { info with
                Name =
                    match info.Name with
                    | CmdName.FullName(shortName, _)
                    | CmdName.ShortName shortName when not (String.IsNullOrEmpty shortName) -> CmdName.FullName(shortName, x)
                    | _ -> CmdName.LongName x
            }
        )


    [<CustomOperation "description">]
    member inline _.description([<InlineIfLambda>] build: BuildCmdInfo, x: string) =
        BuildCmdInfo(fun info -> { build.Invoke(info) with Description = Some x })

    [<CustomOperation "value">]
    member inline _.value([<InlineIfLambda>] build: BuildCmdInfo, x: string) = BuildCmdInfo(fun info -> { build.Invoke(info) with Values = [ x ] })

    [<CustomOperation "acceptValues">]
    member inline _.acceptValues([<InlineIfLambda>] build: BuildCmdInfo, values: string list) =
        BuildCmdInfo(fun info -> { build.Invoke(info) with Values = values })

    [<CustomOperation "optional">]
    member inline _.optional([<InlineIfLambda>] build: BuildCmdInfo) = BuildCmdInfo(fun info -> { build.Invoke(info) with IsOptional = true })

/// When any of the added conditions are satisified, the stage will be active
let whenAny = WhenAnyBuilder()
/// When all of the added conditions are satisified, the stage will be active
let whenAll = WhenAllBuilder()
/// When all of the added conditions are not satisified, the stage will be active
let whenNot = WhenNotBuilder()
/// When the cmd is matched, the stage will be active
let whenCmd = WhenCmdBuilder()
