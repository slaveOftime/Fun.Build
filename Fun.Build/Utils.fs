[<AutoOpen>]
module Fun.Build.Internal.Utils

open System
open System.IO
open System.Text


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
        argDescription.Split([| Environment.NewLine; "\n" |], StringSplitOptions.RemoveEmptyEntries) |> Seq.toList

    match descriptions with
    | []
    | [ _ ] -> sprintf "%s%-30s  %s" prefix argInfo argDescription
    | h :: rest ->
        let sb = StringBuilder()
        let prefixPlaceholder = String(' ', prefix.Length)
        let argInfoPlaceholder = String(' ', argInfo.Length)

        sb.AppendLine(sprintf "%s%-30s  %s" prefix argInfo (h.Trim())) |> ignore
        for i, r in List.indexed rest do
            let str = sprintf "%s%-30s  %s" prefixPlaceholder argInfoPlaceholder (r.Trim())
            if i = rest.Length - 1 then
                sb.Append(str) |> ignore
            else
                sb.AppendLine(str) |> ignore
        sb.ToString()


let printCommandOption prefix (argInfo: string) (argDescription: string) = printfn "%s" (makeCommandOption prefix argInfo argDescription)

let printHelpOptions () = printCommandOption "  " "-h, --help" "Show help and usage information"


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
