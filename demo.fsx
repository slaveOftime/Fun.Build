#r "nuget: Cliwrap"
#r "nuget: Spectre.Console"
#r "Fun.Build/bin/Debug/netstandard2.0/Fun.Build.dll"

open Fun.Build


pipeline "Fun.Build" {
    timeout 30 // You can set overall timeout for the pipeline
    timeoutForStep 10 // You can set default timeout for every step in every stage
    timeoutForStage 10 // You can set default timeout for every stage
    envVars [ "envKey", "envValue" ] // You can add or override environment variables
    cmdArgs [ "arg1"; "arg2" ] // You can reset the command args
    stage "Demo1" {
        timeout 30 // You can set default timeout for the stage
        timeoutForStep 30 // You can set default timeout for step under the stage
        envVars [ "envKey", "envValue" ] // You can add or override environment variables
        // Use cmd, so we can encrypt sensitive argument for formatable string
        cmd $"dotnet --version"
        add (fun ctx -> cmd $"""dotnet {"--version"}""")
        add (fun ctx -> async { return cmd $"""dotnet {"--version"}""" })
        // You can run command directly with a string
        run "dotnet --version"
        run (fun ctx -> "dotnet --version")
        run (fun ctx -> async { return "dotnet --version" })
        // You can run async functions
        run (Async.Sleep 1000)
        run (fun _ -> Async.Sleep 1000)
        run (fun _ -> async { return 0 }) // return an exit code to indicate if it successful
        // You can also run sync functions
        run (fun ctx -> ())
        run (fun ctx -> 0) // return an exit code to indicate if it successful
        // You can also use the low level api
        BuildStep(fun ctx -> async { return 0 })
    }
    stage "Demo2" {
        // whenAny, whenNot, whenAll. They can also be composed.
        whenAll {
            branch "master" // Check current branch is master
            whenAny {
                envVar "envKey" // Check has environment variable
                envVar "envKey" "envValue" // Check has environment variable value
                cmdArg "cmdKey" // Check has cmd arg
                cmdArg "cmdKey" "cmdValue" // Check has cmd arg value which should be behind the cmdKey
            }
        }
        paralle
        run "dotnet --version"
        run "dotnet --version"
    }
    post [ // Post stages are optional. It will run even other normal stages are failed.
        stage "Post stage" {
            run (fun _ -> async {
                return 0 // do something
            })
        }
    ]
    // You can have multiple pipelines, sometimes you only want to run it only if the command specified the pipeline name.
    // If this is set to false, then it will always run if you do not specify which pipeline to run. By default it is true.
    // To specify you can do this: dotnet fsi build.fsx -p Fun.Build
    runIfOnlySpecified false
// You can also run it directly
// runImmediate
}
