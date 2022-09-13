open Fun.Build


let checkEnvs =
    stage "Env checks" {
        timeout 5
        paralle false
        envArgs [ "test1", "test1-2"; "test3", "test3-123" ]
        run "dotnet --list-sdks"
        run "dotnet --version"
        run "powershell $Env:test1"
        run "powershell $Env:test2"
        run "powershell $Env:test3"
        run "powershell $Env:build"
    }


pipeline "demo1" {
    timeout 10
    envArgs [ "build", "true" ]
    checkEnvs
    runIfOnlySpecified
}


pipeline "demo2中国" {
    timeout 12
    envArgs [ "test1", "test1-1"; "build", "true" ]
    checkEnvs
    stage "Cleaning up" {
        whenCmdArg "--clean"
        run (
            async {
                printfn "Start finished"
                do! Async.Sleep 1000
                failwith "test"
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
            //whenAll {
            //    branch "master"
            //}
        }
        runWith (fun ctx -> async { printfn "start dev: %s" ctx.Name })
    }
    post [
        stage "clean up" {
            run "echo cleanup"
            //fun _ -> async {
            //    return 1
            //}
        }
    ]
    runIfOnlySpecified false
}
