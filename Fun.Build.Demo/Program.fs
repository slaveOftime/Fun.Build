open Fun.Build


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
    runIfOnlySpecified false
}


pipeline "demo2中国" {
    timeout 12
    envArgs [ "build", "true" ]
    checkEnvs
    stage "Build stuff" {
        whenEnvVar "build" "true"
        run "dotnet restore"
        run "dotnet build"
    }
    stage "Cleaning up" {
        whenCmdArg "--clean"
        //whenAny {
        //    cmdArg ""
        //    envVar ""
        //    branch "master"
        //}
        run (
            async {
                printfn "Start finished"
                do! Async.Sleep 1000
                printfn "finished"
            }
        )
    }
    runIfOnlySpecified false
}
