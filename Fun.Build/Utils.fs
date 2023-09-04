[<AutoOpen>]
module Fun.Build.Internal.Utils

open System
open System.IO
open Fun.Build


let windowsEnvPaths =
    lazy
        (fun () ->
            let envPath = Environment.GetEnvironmentVariable("PATH")
            if String.IsNullOrEmpty envPath |> not then
                envPath.Split Path.PathSeparator |> Seq.toList
            else
                []
        )


let windowsExeExts = [ "exe"; "cmd"; "bat" ]


let makeCommandOption prefix (argInfo: string) (argDescription: string) =
    let descriptions =
        argDescription.Split([| Environment.NewLine; "\n" |], StringSplitOptions.None)
        |> Seq.filter (String.IsNullOrEmpty >> not)
        |> Seq.toList

    match descriptions with
    | [] -> sprintf "%s%s" prefix argInfo
    | [ h ] -> sprintf "%s%-30s  %s" prefix argInfo h
    | h :: rest ->
        let prefixPlaceholder = String(' ', prefix.Length)
        let argInfoPlaceholder = String(' ', argInfo.Length)
        let restFormatted =
            rest
            |> List.map (fun r -> sprintf "%s%-30s  %s" prefixPlaceholder argInfoPlaceholder (r.Trim()))
            |> String.concat Environment.NewLine
        sprintf "%s%-30s  %s%s%s" prefix argInfo (h.Trim()) Environment.NewLine restFormatted


let printCommandOption prefix (argInfo: string) (argDescription: string) = printfn "%s" (makeCommandOption prefix argInfo argDescription)

let printHelpOptions () = printCommandOption "  " "-h, --help" "Show help and usage information"


let makeEnvNameForPrint (info: EnvArg) = if info.IsOptional then sprintf "%s [optional]" info.Name else info.Name

let makeCmdNameForPrint mode (info: CmdArg) =
    match mode with
    | Mode.CommandHelp { Verbose = false } ->
        match info.Name with
        | CmdName.ShortName x -> x
        | CmdName.LongName x -> $"    {x}"
        | CmdName.FullName(s, l) -> $"{s}, {l}"
    | _ ->
        match info.Name with
        | CmdName.ShortName x -> x
        | CmdName.LongName x -> x
        | CmdName.FullName(s, l) -> $"{s}, {l}"
    |> if info.IsOptional then sprintf "%s [optional]" else id

let makeValuesForPrint (values: string list) =
    match values with
    | [] -> ""
    | _ -> Environment.NewLine + "[choices: " + String.concat ", " (values |> Seq.map (sprintf "\"%s\"")) + "]"


let getFsiFileName () =
    let args = Environment.GetCommandLineArgs()

    if args.Length >= 2 && args[1].EndsWith(".fsx", StringComparison.OrdinalIgnoreCase) then
        args[1]
    else
        "your_script.fsx"


module ValueOption =

    let inline defaultWithVOption (fn: unit -> 'T voption) (data: 'T voption) = if data.IsSome then data else fn ()

    let inline ofOption (data: 'T option) =
        match data with
        | Some x -> ValueSome x
        | _ -> ValueNone


module Seq =

    let shuffle (seq: seq<'a>) =
        let rnd = System.Random()
        let arry = seq |> Seq.toArray
        Array.sortInPlaceBy (fun _ -> rnd.Next()) arry
        Array.toSeq arry
