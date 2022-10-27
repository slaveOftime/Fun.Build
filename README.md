# Fun.Build [![Nuget](https://img.shields.io/nuget/vpre/Fun.Build)](https://www.nuget.org/packages/Fun.Build)

<p style="color: red;">Current it is under development, use it carefully</p>

This is a project mainly used for CICD, you can use it in a fsharp project or as a script. You can check the **build.fsx** or **demo.fsx** under the root folder to check how the Fun.Build project itself is built and published to nuget.

The basic idea is you have **pipeline** which can contain multiple stages.  
Every **stage** can contain multiple steps. In the stage you can set it to run in parallel or run under some conditions (when envVar, cmdArg, branch etc.).  
Every **step** is just a **async< int >**, int is for the exit code. 


## For what

- Simple and straight forward DSL
- Type safety and extendable DSL
- Build and compose complex pipelines
- Test your pipelines locally


## Example:

```fsharp
#r "nuget: Fun.Build, 0.1.7"

open Fun.Build

pipeline "Fun.Build" {
    timeout 30 // You can set overall timeout for the pipeline
    timeoutForStep 10 // You can set default timeout for every step in every stage
    timeoutForStage 10 // You can set default timeout for every stage
    envVars [ "envKey", "envValue" ] // You can add or override environment variables
    cmdArgs [ "arg1"; "arg2" ] // You can reset the command args
    workingDir __SOURCE_DIRECTORY__
    stage "Demo1" {
        timeout 30 // You can set default timeout for the stage
        timeoutForStep 30 // You can set default timeout for step under the stage
        envVars [ "envKey", "envValue" ] // You can add or override environment variables
        // Use cmd, so we can encrypt sensitive argument for formatable string
        cmd $"dotnet --version"
        run (fun ctx -> cmd $"""dotnet {"--version"}""")
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
        step (fun ctx _ -> async { return Ok() })
        BuildStep(fun ctx _ -> async { return Ok() })
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
        openBrowser "https://github.com/slaveOftime/Fun.Build"
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
```


## Print command help information

You can call **tryPrintPipelineCommandHelp ()** at the end of your script to get some help infomation.  
Then you can run below command to get the help info: 
```bash
dotnet fsi build.fsx -- -h
```

You can also run below command without call **tryPrintPipelineCommandHelp** to get help info for  pipeline which is set with **runIfOnlySpecified**:
```bash 
dotnet fsi build.fsx -- -p your_pipeline -h
```
