﻿open Fun.Build


let checkEnvs =
    stage "Env checks" {
        timeout 5
        parall true
        run "dotnet --list-sdks"
        run "dotnet --version"
    }


pipeline "demo1" {
    timeout 10
    checkEnvs
    runIfOnlySpecified
}


pipeline "demo2中国" {
    timeout 12
    envArgs [ "build", "true" ]
    checkEnvs
    stage "Cleaning up" {
        whenCmdArg "--clean"
        run (
            async {
                printfn "Start finished"
                do! Async.Sleep 1000
                printfn "finished"
            }
        )
    }
    stage "Build stuff" {
        whenAll {
            cmdArg "--build"
            branch "master"
        }
        run "dotnet restore"
        run "dotnet build"
    }
    stage "Start dev" {
        whenAny {
            cmdArg "--dev"
            branch "master"
        }
    }
    runIfOnlySpecified false
}
