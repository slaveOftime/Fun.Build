#r "nuget: Fun.Build, 0.2.6"
#r "nuget: Fake.IO.FileSystem, 5.23.0"

open Fake.IO
open Fake.IO.Globbing.Operators
open Fun.Build


let envCheckStage =
    stage "Check environment" {
        paralle
        run "dotnet --version"
        run "dotnet --list-sdks"
        run "dotnet tool restore"
        run (fun ctx -> printfn $"""GITHUB_ACTION: {ctx.GetEnvVar "GITHUB_ACTION"}""")
    }

let lintStage =
    stage "Lint" {
        stage "Format" {
            whenNot { envVar "GITHUB_ACTION" }
            run "dotnet fantomas . -r"
        }
        stage "Check" {
            whenEnvVar "GITHUB_ACTION"
            run "dotnet fantomas . -r --check"
        }
    }

let testStage = stage "Run unit tests" { run "dotnet test" }


pipeline "Fun.Build" {
    description "Build and deploy to nuget"
    envCheckStage
    lintStage
    testStage
    stage "Build packages" { run "dotnet pack -c Release Fun.Build/Fun.Build.fsproj -o ." }
    stage "Publish packages to nuget" {
        whenAll {
            branch "master"
            whenAny {
                envVar "NUGET_API_KEY"
                cmdArg "NUGET_API_KEY"
            }
        }
        run (fun ctx ->
            let key = ctx.GetCmdArgOrEnvVar "NUGET_API_KEY"
            cmd $"""dotnet nuget push *.nupkg -s https://api.nuget.org/v3/index.json --skip-duplicate -k {key}"""
        )
    }
    post [
        stage "Post stage" {
            whenNot { envVar "GITHUB_ACTION" }
            run (fun _ -> File.deleteAll !! "*.nupkg")
        }
    ]
    runIfOnlySpecified false
}

pipeline "test" {
    description "Run tests"
    envCheckStage
    lintStage
    testStage
    runIfOnlySpecified
}


tryPrintPipelineCommandHelp ()
