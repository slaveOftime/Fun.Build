[<AutoOpen>]
module Fun.Build.ProcessExtensions

#nowarn "9"

open System
open System.IO
open System.Text
open System.Threading
open System.Diagnostics
open System.Runtime.InteropServices
open Spectre.Console
open Fun.Build.Internal

module private Native =

    module private Windows =

        let terminate (p: Process) =
            try
                if p.CloseMainWindow() then
                    Ok p.ExitCode
                else
                    Error "Process has no main window or the main window is disabled"
            with ex ->
                Error ex.Message

    module private Unix =
        open Microsoft.FSharp.NativeInterop

        [<DllImport("libc", SetLastError = true)>]
        extern int private kill(int pid, int signal)

        [<DllImport("libc", SetLastError = true)>]
        extern int private strerror_r(int errnum, char* buf, UInt64 buflen)

        let private getErrorMessage errno =
            let buffer = NativePtr.stackalloc<char> 1024
            let result = strerror_r (errno, buffer, 1024UL)

            if result = 0 then
                Marshal.PtrToStringAnsi(buffer |> NativePtr.toNativeInt)
            else
                $"errno %i{errno}"

        [<Literal>]
        let private SIGTERM = 15

        [<Literal>]
        let private ESRCH = 3

        let terminate (p: Process) =
            try
                let code = kill (p.Id, SIGTERM)
                let errno = Marshal.GetLastWin32Error()
                if code = -1 && errno <> ESRCH then // ESRCH = process does not exist, assume it exited
                    getErrorMessage errno |> Error
                else
                    Ok code
            with ex ->
                Error ex.Message

    let kill (p: Process) =
        let result =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                Windows.terminate p
            else
                Unix.terminate p

        match result with
        | Ok _ -> ()
        | Error _ -> p.Kill()


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


    static member StartAsync
        (
            startInfo: ProcessStartInfo,
            commandLogString: string,
            logPrefix: string,
            ?printOutput,
            ?captureOutput,
            ?cancellationToken: CancellationToken
        ) =
        async {
            let printOutput = defaultArg printOutput true
            let captureOutput = defaultArg captureOutput false
            let noPrefix = String.IsNullOrEmpty logPrefix
            // We want to redirect output if
            // 1. Use want to add prefix to the output
            // 2. User asked to not print output
            // 3. User asked to capture output
            let shouldRedirectOutput = not noPrefix || not printOutput || captureOutput

            // By default, we don't redirect output because redirecting the
            // output lose the color information.
            if shouldRedirectOutput then
                // We redirect both standard output and error output
                // because some process write mixed output to both...
                // This should in theory avoid losing information because of the redirection.
                startInfo.RedirectStandardOutput <- true
                startInfo.RedirectStandardError <- true
                startInfo.StandardOutputEncoding <- Encoding.UTF8
                startInfo.StandardErrorEncoding <- Encoding.UTF8

            use result = Process.Start startInfo
            let standardOutputSb = StringBuilder()

            let handleDataReceived (ev: DataReceivedEventArgs) =
                if captureOutput then standardOutputSb.AppendLine ev.Data |> ignore
                if printOutput && not (String.IsNullOrEmpty ev.Data) then
                    if noPrefix then
                        Console.WriteLine(ev.Data)
                    else
                        Console.WriteLine(logPrefix + " " + ev.Data)

            if shouldRedirectOutput then
                result.OutputDataReceived.Add handleDataReceived
                result.ErrorDataReceived.Add handleDataReceived

            use! cd =
                Async.OnCancel(fun _ ->
                    AnsiConsole.Markup $"[yellow]{logPrefix}[/] "
                    AnsiConsole.WriteLine $"{commandLogString} is cancelled or timed out and the process will be killed."
                    Native.kill result
                )

            use _ =
                match cancellationToken with
                | Some ct ->
                    ct.Register(fun () ->
                        AnsiConsole.MarkupLine("[yellow]Command is cancelled by your token[/]")
                        Native.kill result
                    )
                    :> IDisposable
                | _ ->
                    { new IDisposable with
                        member _.Dispose() = ()
                    }

            if shouldRedirectOutput then result.BeginOutputReadLine()

            result.WaitForExit()

            return struct {|
                ExitCode = result.ExitCode
                StandardOutput = standardOutputSb.ToString()
            |}
        }
