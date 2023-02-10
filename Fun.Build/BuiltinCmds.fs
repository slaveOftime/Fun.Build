[<AutoOpen>]
module Fun.Build.BuiltinCmds

open System
open System.Text
open Spectre.Console
open System.Diagnostics
open System.Runtime.InteropServices


type StageContext with

    /// Build a ProcessStartInfo object for a command string. If your command is a file path with white space, you should quote it with ' or ".
    member ctx.BuildCommand(commandStr: string) =
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

        ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command.WorkingDirectory <- x)

        ctx.BuildEnvVars() |> Map.iter (fun k v -> command.Environment[ k ] <- v)

        command.StandardOutputEncoding <- Encoding.UTF8
        command.RedirectStandardOutput <- true
        command


    /// Run a command string with current context
    member ctx.RunCommand(commandStr: string, ?step: int) = async {
        let command = ctx.BuildCommand(commandStr)
        let noPrefixForStep = ctx.GetNoPrefixForStep()
        let prefix =
            if noPrefixForStep then ""
            else
                match step with
                | Some i -> ctx.BuildStepPrefix i
                | None -> ctx.GetNamePath()

        if not noPrefixForStep then
            AnsiConsole.Markup $"[green]{prefix}[/] "
        AnsiConsole.MarkupLine $"{commandStr}"

        let! exitCode = Process.StartAsync(command, commandStr, prefix)
        return ctx.MapExitCodeToResult exitCode
    }


    /// Run a command string with current context, and encrypt the string for logging
    member ctx.RunSensitiveCommand(commandStr: FormattableString, ?step: int) = async {
        let command = ctx.BuildCommand(commandStr.ToString())
        let noPrefixForStep = ctx.GetNoPrefixForStep()
        let args: obj[] = Array.create commandStr.ArgumentCount "*"
        let encryptiedStr = String.Format(commandStr.Format, args)

        let prefix =
            if noPrefixForStep then ""
            else
                match step with
                | Some i -> ctx.BuildStepPrefix i
                | None -> ctx.GetNamePath()

        if not noPrefixForStep then
            AnsiConsole.Markup $"[green]{prefix}[/] "
        AnsiConsole.MarkupLine $"{encryptiedStr}"

        let! exitCode = Process.StartAsync(command, encryptiedStr, prefix)
        return ctx.MapExitCodeToResult exitCode
    }


    /// Add command to context
    member ctx.AddCommandStep(commandStrFn: StageContext -> Async<string>) =
        { ctx with
            Steps =
                ctx.Steps
                @ [
                    Step.StepFn(fun (ctx, i) -> async {
                        let! commandStr = commandStrFn ctx
                        return! ctx.RunCommand(commandStr, i)
                    }
                    )
                ]
        }


    member ctx.OpenBrowser(url: string, ?step: int) = async {
        let noPrefixForStep = ctx.GetNoPrefixForStep()

        if noPrefixForStep then
            AnsiConsole.MarkupLine "Open {url} in browser"
        else
            let prefix =
                match step with
                | Some i -> ctx.BuildStepPrefix i
                | None -> ctx.GetNamePath()
            AnsiConsole.MarkupLine $"{prefix} Open {url} in browser"
        
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
let cmd (commandStr: FormattableString) = BuildStep(fun ctx i -> ctx.RunSensitiveCommand(commandStr, i))

/// Create a command with a formattable string which will encode the arguments as * when print to console.
let runSensitive (commandStr: FormattableString) = BuildStep(fun ctx i -> ctx.RunSensitiveCommand(commandStr, i))

/// Open url in browser
let openBrowser (url: string) = BuildStep(fun ctx i -> ctx.OpenBrowser(url, i))
