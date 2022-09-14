[<AutoOpen>]
module Fun.Build.ConditionsBuilder


type ConditionsBuilder() =

    member inline _.Yield(_: unit) = BuildConditions(fun _ x -> x)

    member inline _.Yield(x: BuildStageIsActive) = x

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildConditions) = BuildConditions(fun ctx conds -> fn().Invoke(ctx, conds))
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) = BuildConditions(fun ctx conds -> conds @ [ fn().Invoke(ctx) ])


    member inline _.Combine([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] buildStageIsActive: BuildStageIsActive) =
        BuildConditions(fun ctx conditions -> builder.Invoke(ctx, conditions) @ [ buildStageIsActive.Invoke ctx ])

    member inline this.Combine([<InlineIfLambda>] buildStageIsActive: BuildStageIsActive, [<InlineIfLambda>] builder: BuildConditions) =
        this.Combine(builder, buildStageIsActive)


    member inline this.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        this.Combine(builder, fn ())


    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, ?envValue: string) =
        BuildConditions(fun ctx conditions -> builder.Invoke(ctx, conditions) @ [ ctx.WhenEnvArg(envKey, defaultArg envValue "") ])

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKey: string, ?argValue: string) =
        BuildConditions(fun ctx conditions -> builder.Invoke(ctx, conditions) @ [ ctx.WhenCmdArg(argKey, defaultArg argValue "") ])

    [<CustomOperation("branch")>]
    member inline _.branch([<InlineIfLambda>] builder: BuildConditions, branch: string) =
        BuildConditions(fun ctx conditions -> builder.Invoke(ctx, conditions) @ [ ctx.WhenBranch(branch) ])


type WhenAnyBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun stage -> fun () -> builder.Invoke(stage, []) |> Seq.exists (fun fn -> fn ()))


type WhenAllBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun stage -> fun () -> builder.Invoke(stage, []) |> Seq.map (fun fn -> fn ()) |> Seq.reduce (fun x y -> x && y))


/// When any of the added conditions are satisified, the stage will be active
let whenAny = WhenAnyBuilder()
/// When all of the added conditions are satisified, the stage will be active
let whenAll = WhenAllBuilder()
