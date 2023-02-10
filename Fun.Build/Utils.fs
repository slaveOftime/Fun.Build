namespace Fun.Build

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open Spectre.Console


[<AutoOpen>]
module internal Utils =

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


[<AutoOpen>]
module ProcessExtensions =
    type Process with

        static member GetQualifiedFileName(cmd: string) =
            if
                not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                || Path.IsPathRooted(cmd)
                || not (String.IsNullOrWhiteSpace(Path.GetExtension cmd))
            then
                cmd
            else
                seq {
                    use ps = Process.GetCurrentProcess()
                    if ps.MainModule <> null then ps.MainModule.FileName

                    Directory.GetCurrentDirectory()

                    yield! windowsEnvPaths.Value()
                }
                |> Seq.tryPick (fun path ->
                    windowsExeExts
                    |> Seq.tryPick (fun ext ->
                        let file = Path.ChangeExtension(Path.Combine(path, cmd), ext)
                        if File.Exists file then Some file else None
                    )
                )
                |> Option.defaultValue cmd


        static member StartAsync(startInfo: ProcessStartInfo, commandLogString: string, logPrefix: string) = async {
            use result = Process.Start startInfo
            let noPrefix = String.IsNullOrEmpty logPrefix
            result.OutputDataReceived.Add(fun e ->
                if noPrefix then
                    Console.WriteLine(e.Data)
                else
                    Console.WriteLine(logPrefix + " " + e.Data)
            )

            use! cd =
                Async.OnCancel(fun _ ->
                    if not noPrefix then AnsiConsole.Markup $"[yellow]{logPrefix}[/] "
                    AnsiConsole.WriteLine $"{commandLogString} is cancelled or timeouted and the process will be killed."
                    result.Kill()
                )

            result.BeginOutputReadLine()
            result.WaitForExit()

            return result.ExitCode
        }
