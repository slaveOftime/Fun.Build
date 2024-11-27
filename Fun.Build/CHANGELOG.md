# Changelog

## [Unreleased]

## [1.1.15] - 2024-11-27

- Divide cmd args into CmdArgs and RemainingCmdArgs by **--**, [request](https://github.com/slaveOftime/Fun.Build/issues/81)

## [1.1.14] - 2024-10-21

- PipelineFailedException and PipelineCancelledException should fail fast

## [1.1.13] - 2024-10-21

- Improve error reporting in github actions

## [1.1.12] - 2024-10-21

- Improve error reporting in github actions

## [1.1.11] - 2024-10-21

- Improve error reporting in github actions

## [1.1.10] - 2024-10-21

- Improve error reporting in github actions

## [1.1.9] - 2024-10-18

- Support disablePrintOutput for RunCommand

## [1.1.8] - 2024-10-11

- Fix RunSensitiveCommandCaptureOutput

## [1.1.7] - 2024-09-11

- Add overloads for **when' (stage "some condition" {})**
- Add helper CE builder **whenStage**

## [1.1.6] - 2024-07-22

- Add RunSensitiveCommandCaptureOutput
- Add concurrent as alias of paralle

## [1.1.5] - 2024-07-01

- Improve process kill
- Should cancel other steps if some steps are cancelled and ContinueStepsOnFailure is set to false

## [1.1.4] - 2024-06-24

- Add runHttpHealthCheck

## [1.1.3] - 2024-06-24

- Support cancellationToken for stage's command steps

## [1.1.2] - 2024-02-20

- Improve errors printing

## [1.1.1] - 2024-02-09

- Support continueStepsOnFailure and continueStageOnFailure for stage
- Improve errors printing

## [1.1.0] - 2024-01-31

- Support continueOnStepFailure for stage

## [1.0.9] - 2024-01-22

- Add when' for ConditionsBuilder

## [1.0.8] - 2024-01-19

- Add whenGithubAction

## [1.0.7] - 2024-01-19

- Add indicator for default pipeline
- Add Github module to provide some helper ce operations: collapseGithubActionLogs for pipeline, githubAction for condition

## [1.0.6] - 2024-01-17

- Fix typo
- Fix ctrl+c termination issue

## [1.0.5] - 2023-11-22

- Add runBeforeEachStage and runAfterEachStage
- Enable for loop and yield! for sub stages

## [1.0.4] - 2023-11-17

- Allow optional workingDir for ctx.RunCommand

## [1.0.3] - 2023-11-08

- Fix command capture new line issue

## [1.0.2] - 2023-10-19

- Improve docs
- Improve logs

## [1.0.1] - 2023-10-19

- Add failIfNoActiveSubStage to stage
- Add Changelog helper
- Add Windows IIS deploy helper

## [1.0.0] - 2023-09-26

- Add GetAllCmdArgs to StageContext
- Add GetAllEnvVars to StageContext

## [0.5.3] - 2023-09-14

- Fix: help information is not printed for whenNot 
- Support whenBranches 

## [0.5.2] - 2023-09-07

- Change IsParallel from bool to **StageContext -> bool**

## [0.5.1] - 2023-09-05

- Fix whenBranch: standard output redirect is not enabled

## [0.5.0] - 2023-09-04

- Allow variable declaration inside pipeline and stage

## [0.4.9] - 2023-09-04

- Support EnvArg for envVar operation
- Refactor code

## [0.4.8] - 2023-09-04

- Conserve output format when possible

## [0.4.7] - 2023-09-04

- Ignore empty values when printing choices

## [0.4.6] - 2023-09-04

- Fix help info printing
- Add optional flag to env when printing help info

## [0.4.5] - 2023-09-04

- Should combine all values from duplicate arg
- Improve top level condition help infomation printing

## [0.4.4] - 2023-09-04

- Support EnvArg for whenEnvVar
- Add help methods to build EnvArg
- For top level condition of stage or pipeline it should combine all condition with && rule
- Clean extra line for printing help info

## [0.4.3] - 2023-09-03

- Fix ConditionsBuilder sequence issue
- Make optional help info more explicity
- Add whenEnv and enable print env vars in command help mode

## [0.4.2] - 2023-07-08

- Support CmdArg directly for whenCmdArg and cmdArg
- Simplify printing

## [0.4.1] - 2023-07-08

- Fix missing information for pipeline options/conditions
- Do not print duplicate cmd info in none verbose mode

## [0.4.0] - 2023-06-26

Tidy up print format and simplify color

## [0.3.9] - 2023-06-08

Move runSensitive as a CE operator from BuildinCmds

## [0.3.8] - 2023-03-27

- Make case insensitive for run specified pipeline
- Print error message when specified pipeline is not found
- Fix printing command information for choices

## [0.3.7] - 2023-03-08

- Change NoPrefixForStep default to true, because it is a little verbose
- Expose some helper apis for conditions with StageContext
- Improve markup print

## [0.3.6] - 2023-02-20

- Improve help printing for env condition
- Print detail info when stage is ignored but user mark it FailIfIgnored

## [0.3.5] - 2023-02-18

- Fix ConditionsBuilder order issue
- Fix command help format

## [0.3.4] - 2023-02-18

- Add noStdRedirectForStep
- Add shuffleExecuteSequence

## [0.3.3] - 2023-02-18

- Add whenCmd for more complex commandline use cases
- Add SoftCancelStep and SoftCancelStage to the StageContext
- Tidy up to make the default public APIs cleaner

## [0.3.2] - 2023-02-15

- Add Obsolete to cmd
- Add RunCommandCaptureOutput to StageContext
- Improve inline by using overloads instead of option parameters
- Do not throw for PipelineCancelledException in runIfOnlySpecified

## [0.3.1] - 2023-02-10

- Do not throw for PipelineCancelledException in runIfOnlySpecified

## [0.3.0] - 2023-02-10

- Add noPrefixForStep to make the command print cleaner: https://github.com/slaveOftime/Fun.Build/issues/22
- Use Environment.Exit(1) instead of throw exception: https://github.com/slaveOftime/Fun.Build/issues/25

## [0.2.9] - 2022-11-21

Add failIfIgnored to stage

## [0.2.8] - 2022-11-11

Fix pipeline failing message

## [0.2.7] - 2022-11-11

Add Verification mode for better error displaying

## [0.2.6] - 2022-11-11

Support add verification rule to pipeline

```fsharp
pipeline "pipeline-verify-demo" {
    description "Verify before pipeline start running"
    // Will throw exception when verification failed. The last rule will take effect. Below we set it for multiple times just for demo purpose.
    // You can define your own logic
    verify (fun ctx -> false)
    // To keep consistence, the condition is similar like when building stage
    whenCmdArg "verify"
    whenAll {
        cmdArg "v1"
        branch "verify"
    }
    runIfOnlySpecified
}
```

## [0.2.5] - 2022-11-04

- No need to print command options for whenNot
- Improve verbose command line help info
- Update docs

## [0.2.4] - 2022-10-31

Improve command help format

## [0.2.3] - 2022-10-28

- Refactor
- Expose RunCommand, RunSensitiveCommand for StageContext

## [0.2.2] - 2022-10-27

Expose ProcessExtensions

## [0.2.1] - 2022-10-27

- Improve CommandHelp mode
- Add --verbose for CommandHelp mode
- Support command file name with witespace, eg: run "'some complex path.exe' -h"

## [0.2.0] - 2022-10-26

Support CommandHelp mode for pipeline, you can put printPipelineCommandHelpIfNeeded() at the end of your script to get some help infomation. 
Then you can run below command to get the help info: 
dotnet fsi build.fsx -- -h

You can also run below command without use printPipelineCommandHelpIfNeeded() to get help info for specific pipeline: 
dotnet fsi build.fsx -- -p your_pipeline_name -h

## [0.1.9] - 2022-10-13

### Added
* Introduce [Keep a changelog](https://keepachangelog.com/)
* Do not print command string with AnsiConsole markup to avoid some formatting error

## [0.1.8] - 2022-09-28

### Changed
* Update FSharp.Core to 6.0.0
