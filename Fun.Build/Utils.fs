[<AutoOpen>]
module Fun.Build.Internal.Utils

open System
open System.IO


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


let makeCommandOption prefix (argInfo: string) (argDescription: string) = sprintf "%s%-30s  %s" prefix argInfo argDescription

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
