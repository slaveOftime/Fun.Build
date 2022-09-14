[<AutoOpen>]
module Fun.Build.ConditionsBuilder


type ConditionsBuilder() =

    member inline _.Yield(_: unit) = BuildConditions(fun x -> x)

    member inline _.Yield(x: BuildStageIsActive) = x

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildConditions) = BuildConditions(fun conds -> fn().Invoke(conds))
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) = BuildConditions(fun conds -> conds @ [ fn().Invoke ])


    member inline _.Combine([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] buildStageIsActive: BuildStageIsActive) =
        BuildConditions(fun conditions -> builder.Invoke(conditions) @ [ buildStageIsActive.Invoke ])

    member inline this.Combine([<InlineIfLambda>] buildStageIsActive: BuildStageIsActive, [<InlineIfLambda>] builder: BuildConditions) =
        this.Combine(builder, buildStageIsActive)


    member inline this.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        this.Combine(builder, fn ())


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
