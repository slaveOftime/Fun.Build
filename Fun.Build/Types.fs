namespace rec Fun.Build

open System


type PipelineCancelledException(msg: string) =
    inherit Exception(msg)


type PipelineFailedException =
    inherit Exception

    new(msg: string) = { inherit Exception(msg) }
    new(msg: string, ex: exn) = { inherit Exception(msg, ex) }


/// Everything a finished command handed back. No judgement is made about whether the exit code is acceptable.
type CommandOutput = {
    ExitCode: int
    StandardOutput: string
    StandardError: string
    /// True when the command was killed because your cancellation token fired. Without this a
    /// cancelled command is indistinguishable from a genuine failure: it comes back as 143 on Linux
    /// or -1 on Windows, with whatever output it had managed to produce.
    IsCancelled: bool
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

type CmdArg = {
    Name: CmdName
    Values: string list
    Description: string option
    IsOptional: bool
} with

    static member Create(?shortName: string, ?longName: string, ?description: string, ?values, ?isOptional: bool) = {
        Name =
            match shortName, longName with
            | Some s, Some l -> CmdName.FullName(s, l)
            | Some s, None -> CmdName.ShortName(s)
            | None, Some l -> CmdName.LongName(l)
            | _ -> failwith "shortName or longName cannot be empty at the same time"
        Values = defaultArg values []
        Description = description
        IsOptional = defaultArg isOptional false
    }

    member this.WithValue value = { this with Values = this.Values @ [ value ] }
    member this.WithValue values = { this with Values = this.Values @ values }
    member this.WithDescription x = { this with Description = Some x }
    member this.WithOptional x = { this with IsOptional = x }


type EnvArg = {
    Name: string
    Values: string list
    Description: string option
    IsOptional: bool
} with

    static member Create(name: string, ?description: string, ?values, ?isOptional: bool) = {
        Name = name
        Values = defaultArg values []
        Description = description
        IsOptional = defaultArg isOptional false
    }

    member this.WithName x = { this with Name = x }
    member this.WithValue value = { this with Values = this.Values @ [ value ] }
    member this.WithValue values = { this with Values = this.Values @ values }
    member this.WithDescription x = { this with Description = Some x }
    member this.WithOptional x = { this with IsOptional = x }
