namespace rec Fun.Build

open System
open System.Text
open System.Linq
open System.Diagnostics
open Spectre.Console
open CliWrap


type StageContext = {
    Name: string
    IsActive: StageContext -> bool
    IsParallel: bool
    Timeout: TimeSpan voption
    WorkingDir: string voption
    EnvVars: Map<string, string>
    PipelineContext: ValueOption<PipelineContext>
    Steps: (StageContext -> Async<int>) list
}


type PipelineContext = {
    Name: string
    CmdArgs: string list
    EnvVars: Map<string, string>
    Timeout: TimeSpan voption
    WorkingDir: string voption
    Stages: StageContext list
    PostStages: StageContext list
}


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext

type BuildConditions = delegate of conditions: (StageContext -> bool) list -> (StageContext -> bool) list

type BuildStage = delegate of ctx: StageContext -> StageContext

type BuildStageIsActive = delegate of ctx: StageContext -> bool

type BuildStep = delegate of ctx: StageContext -> Async<int>
