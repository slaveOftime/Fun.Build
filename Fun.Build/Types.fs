namespace rec Fun.Build

open System


[<Struct>]
type Step =
    | StepFn of fn: (StageContext -> Async<int>)
    | StepOfStage of stage: StageContext

[<Struct; RequireQualifiedAccess>]
type StageParent =
    | Stage of stage: StageContext
    | Pipeline of pipeline: PipelineContext

type StageContext = {
    Name: string
    IsActive: StageContext -> bool
    IsParallel: bool
    Timeout: TimeSpan voption
    TimeoutForStep: TimeSpan voption
    WorkingDir: string voption
    EnvVars: Map<string, string>
    ParentContext: StageParent voption
    Steps: Step list
}


type PipelineContext = {
    Name: string
    CmdArgs: string list
    EnvVars: Map<string, string>
    Timeout: TimeSpan voption
    TimeoutForStep: TimeSpan voption
    TimeoutForStage: TimeSpan voption
    WorkingDir: string voption
    Stages: StageContext list
    PostStages: StageContext list
}


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext

type BuildConditions = delegate of conditions: (StageContext -> bool) list -> (StageContext -> bool) list

type BuildStage = delegate of ctx: StageContext -> StageContext

type BuildStageIsActive = delegate of ctx: StageContext -> bool

type BuildStep = delegate of ctx: StageContext -> Async<int>
