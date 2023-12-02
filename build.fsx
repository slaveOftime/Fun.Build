#r "nuget: Fun.Build, 1.0.5"

open Fun.Build
open Fun.Build.Internal


type PipelineBuilder with

    [<CustomOperation "collapseGithubActionLogs">]
    member inline this.collapseGithubActionLogs(build: Internal.BuildPipeline) =
        let build = this.runBeforeEachStage (build, (fun ctx -> if ctx.GetStageLevel() = 0 then printfn $"::group::{ctx.Name}"))
        this.runAfterEachStage (build, (fun ctx -> if ctx.GetStageLevel() = 0 then printfn "::endgroup::"))


let options = {|
    GithubAction = EnvArg.Create("GITHUB_ACTION", description = "Run only in in github action container")
    NugetAPIKey = EnvArg.Create("NUGET_API_KEY", description = "Nuget api key")
|}


let stage_checkEnv =
    stage "Check environment" {
        run "dotnet tool restore"
        run (fun ctx -> printfn $"""github action name: {ctx.GetEnvVar options.GithubAction.Name}""")
    }

let stage_lint =
    stage "Lint" {
        stage "Format" {
            whenNot { envVar options.GithubAction }
            run "dotnet fantomas . -r"
        }
        stage "Check" {
            whenEnvVar options.GithubAction
            run "dotnet fantomas . -r --check"
        }
    }

let stage_test = stage "Run unit tests" { run "dotnet test" }


pipeline "packages" {
    description "Build and deploy to nuget"
    collapseGithubActionLogs
    stage_checkEnv
    stage_lint
    stage_test
    stage "Build packages" {
        run (fun _ ->
            let version =
                Changelog.GetLastVersion(__SOURCE_DIRECTORY__) |> Option.defaultWith (fun () -> failwith "Version is not found")
            $"dotnet pack -c Release Fun.Build/Fun.Build.fsproj -p:PackageVersion={version.Version} -o ."
        )
    }
    stage "Publish packages to nuget" {
        whenBranch "master"
        whenEnvVar options.NugetAPIKey
        whenEnvVar "GITHUB_ENV" "" "Only push packages in github action"
        run (fun ctx ->
            let key = ctx.GetEnvVar options.NugetAPIKey.Name
            ctx.RunSensitiveCommand $"""dotnet nuget push *.nupkg -s https://api.nuget.org/v3/index.json --skip-duplicate -k {key}"""
        )
    }
    runIfOnlySpecified
}

pipeline "test" {
    description "Format code and run tests"
    collapseGithubActionLogs
    stage_checkEnv
    stage_lint
    stage_test
    runIfOnlySpecified
}


tryPrintPipelineCommandHelp ()
