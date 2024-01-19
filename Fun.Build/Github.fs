module Fun.Build.Github

open Fun.Build.Internal
open ConditionsBuilder.Internal

type PipelineBuilder with

    [<CustomOperation "collapseGithubActionLogs">]
    member inline this.collapseGithubActionLogs(build: BuildPipeline) =
        let build = this.runBeforeEachStage (build, (fun ctx -> if ctx.GetStageLevel() = 0 then printfn $"::group::{ctx.Name}"))
        this.runAfterEachStage (build, (fun ctx -> if ctx.GetStageLevel() = 0 then printfn "::endgroup::"))


type ConditionsBuilder with

    /// Check if the current env is github action
    [<CustomOperation("githubAction")>]
    member inline _.githubEnv([<InlineIfLambda>] builder: BuildConditions) =
        buildConditions builder (fun ctx -> ctx.WhenEnvArg("GITHUB_ENV", description = "True when in github action env"))


/// Check if the current env is github action
let whenGithubAction = whenAll { githubAction }
