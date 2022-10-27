namespace rec Fun.Build

open System


type StepIndex = int


[<Struct; RequireQualifiedAccess>]
type Step =
    | StepFn of fn: (StageContext * StepIndex -> Async<Result<unit, string>>)
    | StepOfStage of stage: StageContext


[<Struct; RequireQualifiedAccess>]
type StageParent =
    | Stage of stage: StageContext
    | Pipeline of pipeline: PipelineContext


[<Struct; RequireQualifiedAccess>]
type StageIndex =
    | Step of step: int
    | Stage of stage: int


[<Struct; RequireQualifiedAccess>]
type Mode =
    | Execution
    | CommandHelp of verbose: bool


type StageContext = {
    Name: string
    IsActive: StageContext -> bool
    IsParallel: bool
    Timeout: TimeSpan voption
    TimeoutForStep: TimeSpan voption
    WorkingDir: string voption
    EnvVars: Map<string, string>
    AcceptableExitCodes: Set<int>
    ParentContext: StageParent voption
    Steps: Step list
}


type PipelineContext = {
    Name: string
    Mode: Mode
    CmdArgs: string list
    EnvVars: Map<string, string>
    AcceptableExitCodes: Set<int>
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

type BuildStep = delegate of ctx: StageContext * index: StepIndex -> Async<Result<unit, string>>


type PipelineCancelledException(msg: string) =
    inherit Exception(msg)


type PipelineFailedException =
    inherit Exception

    new(msg: string) = { inherit Exception(msg) }
    new(msg: string, ex: exn) = { inherit Exception(msg, ex) }
