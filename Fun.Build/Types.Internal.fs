namespace rec Fun.Build.Internal

open System

type StepSoftCancelledException(msg: string) =
    inherit Exception(msg)

type StageSoftCancelledException(msg: string) =
    inherit Exception(msg)


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


type CommandHelpContext = {
    Verbose: bool
    /// When we find some CmdInfo we can add it here, with this we can remove duplicates for simple printing
    CmdInfos: Collections.Generic.List<CmdInfo>
}

[<Struct; RequireQualifiedAccess>]
type Mode =
    | Execution
    | CommandHelp of ctx: CommandHelpContext
    | Verification


type StageContext = {
    /// This id will be used internally for finding its index under its parent
    Id: int
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
    NoStdRedirectForStep: bool
    ShuffleExecuteSequence: bool
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
    NoStdRedirectForStep: bool
    Stages: StageContext list
    PostStages: StageContext list
}

[<Struct>]
type CmdInfo = {
    Name: CmdName
    Values: string list
    Description: string option
}

[<Struct; RequireQualifiedAccess>]
type CmdName =
    | ShortName of shortName: string
    | LongName of longName: string
    | FullName of short: string * long: string

    member this.Names =
        match this with
        | ShortName x -> [ x ]
        | LongName x -> [ x ]
        | FullName(s, l) -> [ s; l ]


type BuildPipeline = delegate of ctx: PipelineContext -> PipelineContext

type BuildConditions = delegate of conditions: (StageContext -> bool) list -> (StageContext -> bool) list

type BuildStage = delegate of ctx: StageContext -> StageContext

type BuildStageIsActive = delegate of ctx: StageContext -> bool

type BuildStep = delegate of ctx: StageContext * index: StepIndex -> Async<Result<unit, string>>

type BuildCmdInfo = delegate of CmdInfo -> CmdInfo
