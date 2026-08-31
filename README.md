# Fun.Build [![Nuget](https://img.shields.io/nuget/vpre/Fun.Build)](https://www.nuget.org/packages/Fun.Build)

A type-safe DSL for building CICD pipelines in F#. Use it inside an F# project or as a standalone script (`.fsx`).

See [`build.fsx`](build.fsx) and [`demo.fsx`](demo.fsx) in the repo root for real-world examples of how Fun.Build builds and publishes itself.

## Table of contents

- [Core concepts](#core-concepts)
- [Why use it](#why-use-it)
- [Quick start](#quick-start)
- [Running a pipeline](#running-a-pipeline)
- [Print command line help](#print-command-line-help)
- [API reference](#api-reference)
- [Full example](#full-example)
- [Fun.Build.Cli](#funbuildcli--)

## Core concepts

The whole model is built from three nested building blocks:

| Concept    | Description                                                                                                                                    |
|------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| `pipeline` | A named collection of stages.                                                                                                                                 |
| `stage`    | Groups `step`s (or nested `stage`s). Can run sequentially or in parallel, and be gated behind conditions (env var, cmd arg, branch, platform...). |
| `step`     | The smallest unit of work. It is just an `async<Result<unit, string>>` (the string is the error message).                                       |

Stages run in sequence by default; `paralle`/`concurrent` makes their steps run in parallel. `post` stages run even when the normal stages fail.

[`Fun.Build.Cli`](#funbuildcli--) is a dotnet tool for managing `.fsx` scripts that use Fun.Build with `runIfOnlySpecified`.

## Why use it

- Simple, straightforward DSL
- Type-safe and extensible (add your own operations)
- Build and compose complex pipelines
- Test pipelines locally
- Generate command-line help automatically

## Quick start

```fsharp
#r "nuget: Fun.Build, 1.1.18"
open Fun.Build

pipeline "demo" {
    // pipeline-level configuration first: description, timeout, etc.
    description "A demo pipeline"

    // stages go here
    stage "build" {
        run "dotnet build"          // run a command
        echo "Build finished"       // print a message
    }

    stage "test" {
        run (fun ctx -> "dotnet test")
    }

    // post stages (optional) run even if the stages above fail
    post [ stage "cleanup" { echo "done" } ]

    // only run when selected via `-p demo`
    runIfOnlySpecified
}

// enable `dotnet fsi demo.fsx -- -h`
tryPrintPipelineCommandHelp ()
```

## Running a pipeline

A script can define multiple pipelines. By default (`runIfOnlySpecified`, no argument) a pipeline only runs when explicitly selected, so a single script can hold many pipelines:

```bash
dotnet fsi build.fsx -- -p demo
```

Run several pipelines in one execution:

```bash
dotnet fsi build.fsx -- -p pipeline1 ... -p pipeline2 ...
```

Notes:

- Pass `--` before your args so `dotnet fsi` forwards them to the script instead of consuming them.
- `runIfOnlySpecified false` makes the pipeline the *default*: it runs when no `-p`/`--pipeline` is given.
- `runImmediate` runs the pipeline right away when the script is evaluated, regardless of args.

## Print command line help

Call `tryPrintPipelineCommandHelp ()` at the end of your script, then:

```bash
dotnet fsi build.fsx -- -h
```

To get help for a single `runIfOnlySpecified` pipeline without calling the helper:

```bash
dotnet fsi build.fsx -- -p your_pipeline -h
```

Use `-v` / `--verbose` for verbose output (pipeline structure, conditions and options detail).

## API reference

### Pipeline operations (`pipeline { ... }`)

| Operation             | Purpose                                                                                     |
|-----------------------|---------------------------------------------------------------------------------------------|
| `description`         | Description used for command help.                                                           |
| `verify`              | Runs before the pipeline; throws when it returns `false`.                                    |
| `timeout`             | Overall timeout (seconds, or a `TimeSpan`).                                                  |
| `timeoutForStage`     | Default timeout for every stage.                                                             |
| `timeoutForStep`      | Default timeout for every step in every stage.                                               |
| `envVars`             | Add/override environment variables.                                                          |
| `acceptExitCodes`     | Extra exit codes treated as success (default is `0`).                                        |
| `cmdArgs`             | Reset command-line args (defaults to `Environment.GetCommandLineArgs()`).                    |
| `workingDir`          | Working directory for all steps.                                                             |
| `noPrefixForStep`     | Disable the step prefix when printing.                                                       |
| `noStdRedirectForStep`| Do not print external command stdout/stderr.                                                 |
| `runBeforeEachStage`  | Hook executed before every stage.                                                            |
| `runAfterEachStage`   | Hook executed after every stage (even on failure).                                           |
| `post`                | `StageContext list` run after normal stages, even on failure.                                |
| `runImmediate`        | Run the pipeline immediately.                                                                |
| `runIfOnlySpecified`  | Run only when selected via `-p <name>` (default `true`; `false` = default pipeline).         |

### Stage operations (`stage { ... }`)

| Operation                 | Purpose                                                                 |
|---------------------------|-------------------------------------------------------------------------|
| `envVars`                 | Add/override environment variables.                                      |
| `acceptExitCodes`         | Extra exit codes treated as success.                                     |
| `failIfIgnored`           | Throw if the stage is inactive.                                          |
| `failIfNoActiveSubStage`  | Throw if no nested sub-stage is active.                                  |
| `continueStepsOnFailure`  | Continue remaining steps after a step fails.                             |
| `continueStageOnFailure`  | Mark the stage successful even when a step fails.                        |
| `continueOnStepFailure`   | Set both `continueStepsOnFailure` and `continueStageOnFailure`.          |
| `timeout`                 | Timeout for the stage (seconds or `TimeSpan`).                           |
| `timeoutForStep`          | Timeout for every step under the stage.                                  |
| `paralle` / `concurrent`  | Run steps in parallel (`concurrent` is an alias of `paralle`).           |
| `workingDir`              | Working directory for all steps.                                         |
| `noPrefixForStep`         | Disable step prefix.                                                     |
| `noStdRedirectForStep`    | Do not print external command output.                                    |
| `shuffleExecuteSequence`  | Shuffle the order steps execute.                                         |
| `run`                     | Add a step (many overloads, see below).                                  |
| `runSensitive`            | Run a command, masking format arguments when printing.                   |
| `runHttpHealthCheck`      | Poll a URL until it returns a success status.                            |
| `echo`                    | Print a message (string or `StageContext -> string`).                   |
| `step`                    | Low-level step: `fun ctx i -> async { return Ok () }`.                   |
| `openBrowser`             | Open a URL in the default browser.                                       |

### `run` overloads (step types)

`run` accepts the following shapes; results are mapped through `acceptExitCodes`:

- a command string or `StageContext -> string` / `StageContext -> Async<string>`
- `Async<unit>` / `Async<int>` (int = exit code)
- `StageContext -> unit` / `StageContext -> int`
- `StageContext -> Async<unit>` / `StageContext -> Async<int>`
- `StageContext -> Result<unit, string>` / `Async<Result<...>>` / `Task<...>`
- `StageContext -> Task` / `Task<unit>` / `Task<int>`

### Conditions (`whenAny`, `whenAll`, `whenNot`, plus `whenCmd` / `whenEnv` / `whenStage`)

Stages and pipelines can be gated with `when*` operations, composable via `whenAny`, `whenAll`, `whenNot`:

```fsharp
stage "deploy" {
    whenBranch "master"
    whenAny {
        envVar "CI"                          // has env var
        envVar "MY_KEY" "value"              // env var with a value
        cmdArg "--release"                   // has cmd arg
        cmdArg "--env" "prod" "desc"         // cmd arg with a value
        platformLinux                        // on Linux
        platformWindows                      // on Windows
        when' (stage "Check" { run (fun _ -> Ok ()) }) // dynamic check via a stage
    }
    run "dotnet publish"
}
```

Available conditions inside `whenAny`/`whenAll`/`whenNot`:

| Condition            | Purpose                                          |
|----------------------|--------------------------------------------------|
| `when'`              | Static `bool` or result of a `stage`.             |
| `envVar`             | Match an env var (optional value/description/isOptional). |
| `cmdArg`             | Match a cmd arg (optional value/description/isOptional). |
| `branch` / `branches`| Match the current git branch.                     |
| `platformWindows` / `platformLinux` / `platformOSX` | Match the OS. |

Pipeline/stage level equivalents (same behavior, different names):

| Pipeline/stage          | Inside `whenAny`/`whenAll`/`whenNot` |
|-------------------------|--------------------------------------|
| `whenEnvVar`            | `envVar`                             |
| `whenCmdArg`            | `cmdArg`                             |
| `whenBranch` / `whenBranches` | `branch` / `branches`           |
| `whenWindows` / `whenLinux` / `whenOSX` | `platformWindows` / `platformLinux` / `platformOSX` |
| `when'`                 | `when'`                              |

`whenCmd` builds a richer cmd option with name/description/values for help generation:

```fsharp
stage "watch" {
    whenCmd {
        fullName "-w" "--watch"
        description "watch cool stuff"
    }
    run "dotnet run"
}
```

`whenEnv` mirrors it for environment variables. `whenStage "name" { ... }` treats a stage's result as a condition.

### Context helpers (available via `ctx` in a step)

| Helper                         | Purpose                                                            |
|--------------------------------|--------------------------------------------------------------------|
| `ctx.RunCommand`               | Run a command string.                                              |
| `ctx.RunCommandCaptureOutput`  | Run a command and return captured stdout.                          |
| `ctx.RunSensitiveCommand`      | Run a command, masking args in logs.                               |
| `ctx.RunSensitiveCommandCaptureOutput` | Sensitive command + capture stdout.                       |
| `ctx.OpenBrowser`              | Open a URL in the browser.                                         |
| `ctx.GetEnvVar` / `TryGetEnvVar`        | Read an env var (default `""` / `ValueOption`).          |
| `ctx.GetCmdArg` / `TryGetCmdArg`        | Read a cmd arg value.                                    |
| `ctx.GetCmdArgOrEnvVar` / `TryGetCmdArgOrEnvVar` | Read a value from cmd arg then env var.    |
| `ctx.GetAllCmdArgs` / `GetRemainingCmdArgs`    | Args before / after `--`.                                 |
| `ctx.GetWorkingDir`            | Effective working directory.                                       |
| `ctx.SoftCancelStep` / `SoftCancelStage`      | Cancel step/stage but mark it successful.        |
| `ctx.RunHttpHealthCheck`       | Poll a URL until healthy.                                          |

## Full example

This example covers most of the APIs; treat it as living documentation:

```fsharp
#r "nuget: Fun.Build, 1.1.18"

open Fun.Result
open Fun.Build

[<AutoOpen>]
module Extensions =
    open Fun.Build.Internal

    // Example of creating a custom operation.
    type PipelineBuilder with

        [<CustomOperation "collapseGithubActionLogs">]
        member inline this.collapseGithubActionLogs(build: Internal.BuildPipeline) =
            let build =
                this.runBeforeEachStage (build, (fun ctx -> if ctx.GetStageLevel() = 0 then printfn $"::group::{ctx.Name}"))
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
            when' (stage "Check" { run (fun ctx -> Ok()) }) // Check result of a stage, useful for dynamic checks
            whenStage "Check" { run (fun ctx -> Ok()) } // Check result of a stage, useful for dynamic checks
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
        continueOnStepFailure // When set this, the stage will be considered as success even if it's step is failed
        whenCmdArg "arg2"
        echo "Got here!"
    }
    stage "inactive" {
        whenCmdArg "arg3"
        echo "Got here!"
    }
    post [ // Post stages are optional. It will run even other normal stages are failed.
        stage "Post stage" {
            echo "You are finished"
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
        // You can get the cmd from the context
        run (fun ctx -> printfn "%A" (ctx.GetCmdArg("--build")))
    }
    runIfOnlySpecified
}


// This will collect command line help information for you
// You can run: dotnet fsi demo.fsx -- -h
tryPrintPipelineCommandHelp ()
```

## Fun.Build.Cli  [![Nuget](https://img.shields.io/nuget/vpre/Fun.Build.Cli)](https://www.nuget.org/packages/Fun.Build.Cli)

A dotnet tool that manages F# scripts using Fun.Build and `tryPrintPipelineCommandHelp`.

```bash
dotnet tool install --global Fun.Build.Cli
```

```
fun-build -h
```

```bash

Pipelines:

  source                          Manage source directory
    Options(collected from pipeline and stages):
      --list                      List current source directories
      --add                       Add source directory and build pipeline info cache for usage
      --remove                    Remove source from current source list
      --clean                     Clear all the cache files
      --refresh                   Rebuild pipelines and cache for current source again

  run (default)                   Execute pipeline found from sources
    Options(collected from pipeline and stages):
      --use-last-run              Execute the last pipeline
      --with-last-args            Use the last run arugments
```

After the first setup you can run it without arguments; it prompts questions to guide you.

## Donation

If you find my projects helpful and would like to support my work, consider making a donation via PayPal. Your support is greatly appreciated!

<a href="https://paypal.me/wubinwen" style="display: flex; align-items: center; gap: 12px;">
    <img src="https://www.paypalobjects.com/paypal-ui/logos/svg/paypal-color.svg" height="30">
</a>