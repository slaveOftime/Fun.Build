[<AutoOpen>]
module Fun.Build.ConditionsBuilder

open System.Runtime.InteropServices


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
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, ?envValue: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenEnvArg(envKey, defaultArg envValue "")
            ]
        )

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKey: string, ?argValue: string) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun ctx -> ctx.WhenCmdArg(argKey, defaultArg argValue "")
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
                fun _ -> RuntimeInformation.IsOSPlatform OSPlatform.Windows
            ]
        )

    [<CustomOperation("platformLinux")>]
    member inline _.platformLinux([<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun _ -> RuntimeInformation.IsOSPlatform OSPlatform.Linux
            ]
        )

    [<CustomOperation("platformOSX")>]
    member inline _.platformOSX([<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions ->
            builder.Invoke(conditions)
            @ [
                fun _ -> RuntimeInformation.IsOSPlatform OSPlatform.OSX
            ]
        )


type StageBuilder with

    /// Set if stage is active or should run.
    [<CustomOperation("when'")>]
    member inline _.when'([<InlineIfLambda>] build: BuildStage, value: bool) =
        BuildStage(fun ctx -> { build.Invoke ctx with IsActive = fun _ -> value })


    /// Set if stage is active or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, ?envValue: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenEnvArg(envKey, defaultArg envValue "")
            }
        )

    /// Set if stage is active or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKey: string, ?argValue: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenCmdArg(argKey, defaultArg argValue "")
            }
        )

    /// Set if stage is active or should run by check the git branch name.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch([<InlineIfLambda>] build: BuildStage, branch: string) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun ctx -> ctx.WhenBranch branch
            }
        )

    /// Set if stage is active or should run by check the platform is Windows.
    [<CustomOperation("whenWindows")>]
    member inline _.whenWindows([<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun _ -> RuntimeInformation.IsOSPlatform OSPlatform.Windows
            }
        )

    /// Set if stage is active or should run by check the platform is Linux.
    [<CustomOperation("whenLinux")>]
    member inline _.whenLinux([<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun _ -> RuntimeInformation.IsOSPlatform OSPlatform.Linux
            }
        )

    /// Set if stage is active or should run by check the platform is OSX.
    [<CustomOperation("whenOSX")>]
    member inline _.whenOSX([<InlineIfLambda>] build: BuildStage) =
        BuildStage(fun ctx ->
            { build.Invoke ctx with
                IsActive = fun _ -> RuntimeInformation.IsOSPlatform OSPlatform.OSX
            }
        )


type WhenAnyBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx -> builder.Invoke [] |> Seq.exists (fun fn -> fn ctx))


type WhenAllBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx -> builder.Invoke [] |> Seq.map (fun fn -> fn ctx) |> Seq.reduce (fun x y -> x && y))


type WhenNotBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx -> builder.Invoke [] |> Seq.map (fun fn -> not (fn ctx)) |> Seq.reduce (fun x y -> x && y))


/// When any of the added conditions are satisified, the stage will be active
let whenAny = WhenAnyBuilder()
/// When all of the added conditions are satisified, the stage will be active
let whenAll = WhenAllBuilder()
/// When all of the added conditions are not satisified, the stage will be active
let whenNot = WhenNotBuilder()
