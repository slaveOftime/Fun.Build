[<AutoOpen>]
module Fun.Build.ProcessExtensions

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open Spectre.Console
open Fun.Build.Internal

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


    static member StartAsync(startInfo: ProcessStartInfo, commandLogString: string, logPrefix: string, ?printOutput, ?captureOutput) = async {
        use result = Process.Start startInfo
        let noPrefix = String.IsNullOrEmpty logPrefix
        let printOutput = defaultArg printOutput true
        let captureOutput = defaultArg captureOutput false
        let shouldRedirectOutput = printOutput || captureOutput
        let standardOutputSb = System.Text.StringBuilder()

        if shouldRedirectOutput then
            result.OutputDataReceived.Add(fun e ->
                if captureOutput then standardOutputSb.Append e.Data |> ignore
                if printOutput then
                    if noPrefix then
                        Console.WriteLine(e.Data)
                    else
                        Console.WriteLine(logPrefix + " " + e.Data)
            )

        use! cd =
            Async.OnCancel(fun _ ->
                AnsiConsole.Markup $"[yellow]{logPrefix}[/] "

                AnsiConsole.WriteLine $"{commandLogString} is cancelled or timed out and the process will be killed."

                result.Kill()
            )

        if shouldRedirectOutput then result.BeginOutputReadLine()

        result.WaitForExit()

        return struct {|
            ExitCode = result.ExitCode
            StandardOutput = standardOutputSb.ToString()
        |}
    }
