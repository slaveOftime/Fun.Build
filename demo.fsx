#r "nuget: Fun.Result, 0.2.1"
#r "nuget: Spectre.Console, 0.46.0"
#r "Fun.Build/bin/Debug/netstandard2.0/Fun.Build.dll"

open Fun.Result
open Fun.Build

[<AutoOpen>]
module Extensions =
    open Fun.Build.Internal

    type PipelineBuilder with

        [<CustomOperation "collapseGithubActionLogs">]
        member inline this.collapseGithubActionLogs(build: Internal.BuildPipeline) =
            let build = this.runBeforeEachStage (build, (fun ctx -> if ctx.GetStageLevel() = 0 then printfn $"::group::{ctx.Name}"))
            this.runAfterEachStage (build, (fun ctx -> if ctx.GetStageLevel() = 0 then printfn "::endgroup::"))


// You can create a stage and reuse it in any pipeline or nested stages
let demo1 =
    stage "Ways to run something" {
        timeout 30 // You can set default timeout for the stage
        timeoutForStep 30 // You can set default timeout for step under the stage
        envVars [ "envKey", "envValue" ] // You can add or override environment variables
        // Use cmd, so we can encrypt sensitive argument for formatable string
        runSensitive ($"""dotnet {"--version"}""")
        run (fun ctx -> ctx.RunSensitiveCommand $"""dotnet {"--version"}""")
        // You can run command directly with a string
        run "dotnet --version"
        run (fun ctx -> "dotnet --version")
        run (fun ctx -> async { return "dotnet --version" })
        // You use use the RunCommand to run multiple command according to your logics
        run (fun ctx -> asyncResult {
            do! ctx.RunCommand "dotnet --version"
            do! ctx.RunCommand "dotnet --version"
        })
        // You can run async functions
        run (Async.Sleep 1000)
        run (fun _ -> Async.Sleep 1000)
        run (fun _ -> async { return 0 }) // return an exit code to indicate if it successful
        // You can also run sync functions
        run (fun ctx -> ())
        run (fun ctx -> 0) // return an exit code to indicate if it successful
        // You can also use the low level api
        step (fun ctx _ -> async { return Ok() })
    }


pipeline "Fun.Build" {
    description "This is a demo pipeline for docs"
    timeout 30 // You can set overall timeout for the pipeline
    timeoutForStep 10 // You can set default timeout for every step in every stage
    timeoutForStage 10 // You can set default timeout for every stage
    envVars [ "envKey", "envValue" ] // You can add or override environment variables
    cmdArgs [ "arg1"; "arg2" ] // You can reset the command args
    workingDir __SOURCE_DIRECTORY__
    // You can also override the accept exit code for success. By default 0 is for success.
    // But if your external program is using other code you can add it here.
    acceptExitCodes [ 0; 2 ]
    // By default steps will not add prefix for printing information.
    // You can also set the flag on each stage.
    noPrefixForStep false
    // Below is a custom extended operation
    collapseGithubActionLogs
    demo1
    stage "Demo2" {
        // whenAny, whenNot, whenAll. They can also be composed.
        whenBranch "master" // Check current branch is master
        whenAny {
            envVar "envKey" // Check has environment variable
            envVar "envKey" "envValue" // Check has environment variable value
            cmdArg "cmdKey" "" "Check has cmd arg"
            cmdArg "cmdKey" "cmdValue" "Check has cmd arg value which should be behind the cmdKey"
            whenNot { cmdArg "--not-demo" }
        }
        shuffleExecuteSequence // It can shuffle the sequence of steps executing sequence
        run "dotnet --version"
        run "dotnet --list-sdks"
    }
    // You can also nest stages, the stage will be treated as a single stage for parent stage.
    stage "Demo3" {
        stage "Platform" {
            workingDir @"C:\Users"
            whenWindows
            run "powershell pwd"
        }
        stage "Demo nested" {
            shuffleExecuteSequence
            echo "cool nested"
            stage "Deeper" { echo "cooller" }
            stage "inactive" {
                whenCmdArg "arg3"
                echo "Got here!"
            }
        }
        stage "Exit code" {
            acceptExitCodes [ 123 ]
            run (fun _ -> 123)
        }
        // You can open link in browser every easily
        openBrowser "https://github.com/slaveOftime/Fun.Build"
        run (fun ctx -> ctx.OpenBrowser "https://github.com/slaveOftime/Fun.Build")
    }
    stage "FailIfIgnored" {
        failIfIgnored // When set this, the stage cannot be ignored
        whenCmdArg "arg2"
        echo "Got here!"
    }
    stage "inactive" {
        whenCmdArg "arg3"
        echo "Got here!"
    }
    post [ // Post stages are optional. It will run even other normal stages are failed.
        stage "Post stage" {
            echo "You are finished 😂"
            echo (fun ctx -> sprintf "You are finished here: %A" (ctx.GetWorkingDir()))
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


pipeline "pipeline-verify-demo" {
    description "Verify before pipeline start running"

    // Will throw exception when verification failed.
    // You can define your own logic
    verify (fun ctx -> false)
    // To keep consistence, the condition is similar like when building stage
    whenCmdArg "verify"
    whenAny {
        cmdArg "v1"
        branch "master"
    }

    runIfOnlySpecified
}


pipeline "cmd-info" {
    description "Check cmd info build style"
    whenCmd {
        fullName "-w" "--watch"
        // Description can also support multiple lines
        description "watch cool stuff \n dasd asdad \n asdasd as123"
    }
    whenCmd {
        name "--debug"
        description "optional argument"
        optional
    }
    whenEnv {
        name "PRODUCTION"
        description "optional argument"
        optional
    }
    stage "condition demo" {
        noStdRedirectForStep
        failIfIgnored
        // You can use whenCmd CE for more complex situation.
        whenCmd {
            shortName "-w"
            // Description can also support multiple lines
            description "watch cool stuff \n dasd asdad \n asdasd as123"
        }
        whenCmd {
            shortName "-r"
            description "run cool stuff"
            acceptValues [ "v1"; "v2" ]
        }
        whenCmd {
            longName "--build"
            description "build your dream"
            acceptValues [ "v1"; "v2" ]
        }
        whenAny {
            cmdArg "--foo"
            envVar "--bar"
            platformLinux
            platformWindows
            branch "master"
        }
        echo "here we are"
        run "dotnet --list-sdks"
    }
    runIfOnlySpecified
}


// This will collect command line help information for you
// You can run: dotnet fsi demo.fsx -- -h
tryPrintPipelineCommandHelp ()
