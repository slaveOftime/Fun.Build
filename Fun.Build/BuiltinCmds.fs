namespace rec Fun.Build

open System
open System.Threading
open Spectre.Console
open System.Diagnostics
open System.Runtime.InteropServices
open Fun.Result
open Fun.Build.Internal
open Fun.Build.StageContextExtensionsInternal


module BuiltinCmdsInternal =

    type StageContext with

        /// Build a ProcessStartInfo object for a command string. If your command is a file path with white space, you should quote it with ' or ".
        member ctx.BuildCommand(commandStr: string, ?workingDir: string) =
            let index =
                if commandStr.StartsWith "\"" then commandStr.IndexOf "\" "
                else if commandStr.StartsWith "'" then commandStr.IndexOf "' "
                else commandStr.IndexOf " "

            let cmd, args =
                if index > 0 then
                    let cmd = commandStr.Substring(0, index).Replace("\"", "").Replace("'", "").Trim()
                    let args = commandStr.Substring(index + 1).Trim()
                    cmd, args
                else
                    commandStr, ""

            let command = ProcessStartInfo(Process.GetQualifiedFileName cmd, args)

            match workingDir with
            | Some workDir -> command.WorkingDirectory <- workDir
            | None -> ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command.WorkingDirectory <- x)

            ctx.BuildEnvVars() |> Map.iter (fun k v -> command.Environment[k] <- v)

            command


        /// Add command to context
        member ctx.AddCommandStep(commandStrFn: StageContext -> Async<string>, ?cancellationToken: CancellationToken) =
            { ctx with
                Steps =
                    ctx.Steps
                    @ [
                        Step.StepFn(fun (ctx, i) -> async {
                            let! commandStr = commandStrFn ctx
                            return! ctx.RunCommand(commandStr, i, cancellationToken = defaultArg cancellationToken CancellationToken.None)
                        })
                    ]
            }


[<AutoOpen>]
module BuiltinCmds =

    open BuiltinCmdsInternal


    type StageContext with

        /// Run a command string with current context
        member ctx.RunCommand(commandStr: string, ?step: int, ?workingDir: string, ?disablePrintOutput: bool, ?cancellationToken: CancellationToken) = async {
            let disablePrintOutput = defaultArg disablePrintOutput false
            let command = ctx.BuildCommand(commandStr, ?workingDir = workingDir)
            let noPrefixForStep = ctx.GetNoPrefixForStep()
            let prefix =
                if noPrefixForStep then
                    ""
                else
                    match step with
                    | Some i -> ctx.BuildStepPrefix i
                    | None -> ctx.GetNamePath()

            if not noPrefixForStep then AnsiConsole.Markup $"[green]{prefix}[/] "
            AnsiConsole.WriteLine commandStr

            let ct = defaultArg cancellationToken CancellationToken.None

            let! result =
                Process.StartAsync(
                    command,
                    commandStr,
                    prefix,
                    printOutput = (not disablePrintOutput && not (ctx.GetNoStdRedirectForStep())),
                    cancellationToken = ct
                )

            return
                if ct.IsCancellationRequested then
                    Ok()
                else
                    ctx.MapExitCodeToResult result.ExitCode
        }

        /// <summary>
        /// Run a command string with current context, and return the standard output if the exit code is acceptable.
        /// </summary>
        /// <param name="commandStr">Command to run</param>
        /// <param name="step">Current step rank</param>
        /// <param name="workingDir">Working directory for command</param>
        member ctx.RunCommandCaptureOutput
            (
                commandStr: string,
                ?step: int,
                ?workingDir: string,
                ?disablePrintOutput: bool,
                ?cancellationToken: CancellationToken
            ) =
            async {
                let disablePrintOutput = defaultArg disablePrintOutput false
                let command = ctx.BuildCommand(commandStr, ?workingDir = workingDir)
                let noPrefixForStep = ctx.GetNoPrefixForStep()
                let prefix =
                    if noPrefixForStep then
                        ""
                    else
                        match step with
                        | Some i -> ctx.BuildStepPrefix i
                        | None -> ctx.GetNamePath()

                if not noPrefixForStep then AnsiConsole.Markup $"[green]{prefix}[/] "
                AnsiConsole.WriteLine commandStr

                let ct = defaultArg cancellationToken CancellationToken.None

                let! result =
                    Process.StartAsync(
                        command,
                        commandStr,
                        prefix,
                        printOutput = (not disablePrintOutput && not (ctx.GetNoStdRedirectForStep())),
                        captureOutput = true,
                        cancellationToken = ct
                    )

                if ct.IsCancellationRequested then
                    return Ok result.StandardOutput
                else if ctx.IsAcceptableExitCode result.ExitCode then
                    return Ok result.StandardOutput
                else
                    return Error "Exit code is not indicating as successful."
            }


        /// Run a command string with current context, and encrypt the string for logging
        member ctx.RunSensitiveCommandCaptureOutput
            (
                commandStr: FormattableString,
                ?step: int,
                ?workingDir: string,
                ?disablePrintOutput: bool,
                ?cancellationToken: CancellationToken
            ) : Async<Result<string, string>> =
            async {
                let disablePrintOutput = defaultArg disablePrintOutput false
                let command = ctx.BuildCommand(commandStr.ToString(), ?workingDir = workingDir)
                let noPrefixForStep = ctx.GetNoPrefixForStep()
                let args: obj[] = Array.create commandStr.ArgumentCount "*"
                let encryptiedStr = String.Format(commandStr.Format, args)

                let prefix =
                    if noPrefixForStep then
                        ""
                    else
                        match step with
                        | Some i -> ctx.BuildStepPrefix i
                        | None -> ctx.GetNamePath()

                if not noPrefixForStep then AnsiConsole.Markup $"[green]{prefix}[/] "
                AnsiConsole.WriteLine encryptiedStr

                let ct = defaultArg cancellationToken CancellationToken.None

                let! result =
                    Process.StartAsync(
                        command,
                        encryptiedStr,
                        prefix,
                        printOutput = (not disablePrintOutput && not (ctx.GetNoStdRedirectForStep())),
                        captureOutput = true,
                        cancellationToken = ct
                    )

                if ct.IsCancellationRequested then
                    return Ok result.StandardOutput
                else if ctx.IsAcceptableExitCode result.ExitCode then
                    return Ok result.StandardOutput
                else
                    return Error "Exit code is not indicating as successful."
            }

        /// Run a command string with current context, and encrypt the string for logging
        member ctx.RunSensitiveCommand
            (
                commandStr: FormattableString,
                ?step: int,
                ?workingDir: string,
                ?disablePrintOutput: bool,
                ?cancellationToken: CancellationToken
            ) : Async<Result<unit, string>> =
            ctx.RunSensitiveCommandCaptureOutput(
                commandStr,
                ?step = step,
                ?workingDir = workingDir,
                ?disablePrintOutput = disablePrintOutput,
                ?cancellationToken = cancellationToken
            )
            |> AsyncResult.map ignore


        member ctx.OpenBrowser(url: string, ?step: int) = async {
            let noPrefixForStep = ctx.GetNoPrefixForStep()

            if noPrefixForStep then
                AnsiConsole.WriteLine $"Open {url} in browser"
            else
                let prefix =
                    match step with
                    | Some i -> ctx.BuildStepPrefix i
                    | None -> ctx.GetNamePath()
                AnsiConsole.WriteLine $"{prefix} Open {url} in browser"

            try
                Process.Start(url) |> ignore
                return Ok()
            with _ ->
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                    Process.Start(ProcessStartInfo(FileName = "cmd", Arguments = $"/c start {url}", UseShellExecute = true)) |> ignore
                    return Ok()
                else if RuntimeInformation.IsOSPlatform OSPlatform.Linux then
                    Process.Start("xdg-open", url) |> ignore
                    return Ok()
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) then
                    Process.Start("open", url) |> ignore
                    return Ok()
                else
                    return Error "Open url failed. Platform is not supportted."
        }


    /// Create a command with a formattable string which will encode the arguments as * when print to console.
    [<Obsolete("Please use runSensitive instead")>]
    let cmd (commandStr: FormattableString) = BuildStep(fun ctx i -> ctx.RunSensitiveCommand(commandStr, i))

    /// Open url in browser
    let openBrowser (url: string) = BuildStep(fun ctx i -> ctx.OpenBrowser(url, i))
