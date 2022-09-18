[<AutoOpen>]
module internal Fun.Build.Utils

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open Spectre.Console


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


module ValueOption =

    let inline defaultWithVOption (fn: unit -> 'T voption) (data: 'T voption) = if data.IsSome then data else fn ()

    let inline ofOption (data: 'T option) =
        match data with
        | Some x -> ValueSome x
        | _ -> ValueNone


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


    static member StartAsync(startInfo: ProcessStartInfo, commandStr: string, logPrefix: string) = async {
        use result = Process.Start startInfo

        result.OutputDataReceived.Add(fun e -> Console.WriteLine(logPrefix + " " + e.Data))

        use! cd =
            Async.OnCancel(fun _ ->
                AnsiConsole.MarkupLine $"{logPrefix} [yellow]{commandStr}[/] is cancelled or timeouted and the process will be killed."
                result.Kill()
            )

        result.BeginOutputReadLine()
        result.WaitForExit()

        return result.ExitCode
    }
