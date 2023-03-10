# Fun.Build [![Nuget](https://img.shields.io/nuget/vpre/Fun.Build)](https://www.nuget.org/packages/Fun.Build)

<p style="color: red;">Current it is under development, use it carefully</p>

This is a project mainly used for CICD, you can use it in a fsharp project or as a script. You can check the **build.fsx** or **demo.fsx** under the root folder to check how the Fun.Build project itself is built and published to nuget.

The basic idea is you have **pipeline** which can contain multiple stages.  
Every **stage** can contain multiple steps. In the stage you can set it to run in parallel or run under some conditions (when envVar, cmdArg, branch etc.).  
Every **step** is just a **async<Result<unit, string>>**, string is for the error message. 


## For what

- Simple and straight forward DSL
- Type safety and extendable DSL
- Build and compose complex pipelines
- Test your pipelines locally
- Generate command line help information automatically


## Minimal example and conventions

```fsharp
#r "nuget: Fun.Build, 0.3.7"
open Fun.Build

pipeline "demo" {
    // configuration for pipeline itself goes first, like timeout, description etc.
    description "xxxx"
    
    // stages goes here
    stage "foo" {
        // configuration for stage itself goes first
        paralle     
        // steps or nested stages goes here
        run ...
    }
    
    // post stages goes here if you have any
    // post []
    
    // For script command usage, you can add below helper 
    runIfOnlySpecified
}

// For script command usage, you can add below helper
// You can run: dotnet demo.fsx -- -h
tryPrintPipelineCommandHelp ()
```


## Example:

Below example covered most of the apis and usage example, take it as the documents😊:

```fsharp
#r "nuget: Fun.Result"
#r "nuget: Fun.Build, 0.3.7"

open Fun.Result
open Fun.Build

// You can create a stage and reuse it in any pipeline or nested stages
let demo1 =
    stage "Ways to run something" {
        timeout 30 // You can set default timeout for the stage
        timeoutForStep 30 // You can set default timeout for step under the stage
        envVars [ "envKey", "envValue" ] // You can add or override environment variables
        // Use cmd, so we can encrypt sensitive argument for formatable string
        runSensitive $"dotnet --version"
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
    // By default steps will add prefix for printing information, with below flag we can remove it to make it cleaner for some use cases.
    // You can also set the flag on each stage.
    noPrefixForStep
    demo1
    stage "Demo2" {
        // whenAny, whenNot, whenAll. They can also be composed.
        whenAll {
            branch "master" // Check current branch is master
            whenAny {
                envVar "envKey" // Check has environment variable
                envVar "envKey" "envValue" // Check has environment variable value
                cmdArg "cmdKey" "" "Check has cmd arg"
                cmdArg "cmdKey" "cmdValue" "Check has cmd arg value which should be behind the cmdKey"
                whenNot { cmdArg "--not-demo" }
            }
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
            echo "cool nested"
            stage "Deeper" { echo "cooller" }
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
    // Will throw exception when verification failed. The last rule will take effect. Below we set it for multiple times just for demo purpose.
    // You can define your own logic
    verify (fun ctx -> false)
    // To keep consistence, the condition is similar like when building stage
    whenCmdArg "verify"
    whenAll {
        cmdArg "v1"
        branch "master"
    }
    runIfOnlySpecified
}


let demoCondition = whenAll {
    // You can use whenCmd CE for more complex situation.
    whenCmd {
        name "-w"
        alias "--watch"
        // Description can also support multiple lines
        description "watch cool stuff \n dasd asdad \n asdasd as123"
    }
    whenCmd {
        name "run"
        description "run cool stuff"
        acceptValues [ "v1"; "v2" ]
    }
}

pipeline "cmd-info" {
    description "Check cmd info build style"
    demoCondition
    stage "" {
        demoCondition
        echo "here we are"
    }
    runIfOnlySpecified
}


// This will collect command line help information for you
// You can run: dotnet demo.fsx -- -h
tryPrintPipelineCommandHelp ()
```


## Print command line help information

You can call **tryPrintPipelineCommandHelp ()** at the end of your script to get some help infomation.  
Then you can run below command to get the help info: 
```bash
dotnet fsi build.fsx -- -h
```

You can also run below command without call **tryPrintPipelineCommandHelp** to get help info for  pipeline which is set with **runIfOnlySpecified**:
```bash 
dotnet fsi build.fsx -- -p your_pipeline -h
```
