[<AutoOpen>]
module internal Fun.Build.Utils

open System
open System.Diagnostics
open Spectre.Console


module ValueOption =

    let inline defaultWithVOption (fn: unit -> 'T voption) (data: 'T voption) = if data.IsSome then data else fn ()

    let inline ofOption (data: 'T option) =
        match data with
        | Some x -> ValueSome x
        | _ -> ValueNone


type Process with

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
