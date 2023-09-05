#r "nuget: Fun.Build, 0.5.1"
#r "nuget: Fake.IO.FileSystem, 5.23.0"

open Fake.IO
open Fake.IO.Globbing.Operators
open Fun.Build


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


pipeline "Fun.Build" {
    description "Build and deploy to nuget"
    stage_checkEnv
    stage_lint
    stage_test
    stage "Build packages" { run "dotnet pack -c Release Fun.Build/Fun.Build.fsproj -o ." }
    stage "Publish packages to nuget" {
        whenBranch "master"
        whenEnvVar options.NugetAPIKey
        run (fun ctx ->
            let key = ctx.GetEnvVar options.NugetAPIKey.Name
            ctx.RunSensitiveCommand $"""dotnet nuget push *.nupkg -s https://api.nuget.org/v3/index.json --skip-duplicate -k {key}"""
        )
    }
    post [
        stage "Clean packages" {
            whenNot { envVar options.GithubAction }
            run (fun _ -> File.deleteAll !! "*.nupkg")
        }
    ]
    runIfOnlySpecified false
}

pipeline "test" {
    description "Format code and run tests"
    stage_checkEnv
    stage_lint
    stage_test
    runIfOnlySpecified
}


tryPrintPipelineCommandHelp ()
