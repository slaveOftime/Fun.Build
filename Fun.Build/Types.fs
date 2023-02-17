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
    | Verification


type StageContext = {
    Name: string
    IsActive: StageContext -> bool
    IsParallel: bool
    Timeout: TimeSpan voption
    TimeoutForStep: TimeSpan voption
    WorkingDir: string voption
    EnvVars: Map<string, string>
    AcceptableExitCodes: Set<int>
    FailIfIgnored: bool
    NoPrefixForStep: bool
    ParentContext: StageParent voption
    Steps: Step list
}


type PipelineContext = {
    Name: string
    Description: string voption
    Mode: Mode
    /// Verify before run pipeline, will throw PipelineFailedException if return false
    Verify: PipelineContext -> bool
    CmdArgs: string list
    EnvVars: Map<string, string>
    AcceptableExitCodes: Set<int>
    Timeout: TimeSpan voption
    TimeoutForStep: TimeSpan voption
    TimeoutForStage: TimeSpan voption
    WorkingDir: string voption
    NoPrefixForStep: bool
    Stages: StageContext list
    PostStages: StageContext list
}


[<Struct>]
type CmdInfo = {
    Name: string
    Alias: string option
    Values: string list
    Description: string option
}


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext

type BuildConditions = delegate of conditions: (StageContext -> bool) list -> (StageContext -> bool) list

type BuildStage = delegate of ctx: StageContext -> StageContext

type BuildStageIsActive = delegate of ctx: StageContext -> bool

type BuildStep = delegate of ctx: StageContext * index: StepIndex -> Async<Result<unit, string>>

type BuildCmdInfo = delegate of CmdInfo -> CmdInfo


type PipelineCancelledException(msg: string) =
    inherit Exception(msg)


type PipelineFailedException =
    inherit Exception

    new(msg: string) = { inherit Exception(msg) }
    new(msg: string, ex: exn) = { inherit Exception(msg, ex) }
