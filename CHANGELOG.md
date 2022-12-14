# Changelog

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