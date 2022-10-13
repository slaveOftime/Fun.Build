[<AutoOpen>]
module Fun.Build.BuiltinCmds

open System
open Spectre.Console
open System.Diagnostics
open System.Runtime.InteropServices


/// Create a command with a formattable string which will encode the arguments as * when print to console.
let cmd (commandStr: FormattableString) =
    step (fun ctx i -> async {
        let command = ctx.BuildCommand(commandStr.ToString())
        let args: obj[] = Array.create commandStr.ArgumentCount "*"
        let encryptiedStr = String.Format(commandStr.Format, args)

        AnsiConsole.Markup $"[green]{ctx.BuildStepPrefix i}[/] "
        AnsiConsole.MarkupLine $"{encryptiedStr}"

        let! exitCode = Process.StartAsync(command, encryptiedStr, ctx.BuildStepPrefix i)
        return ctx.MapExitCodeToResult exitCode
    }
    )


/// Open url in browser
let openBrowser (url: string) =
    step (fun ctx i -> async {
        let prefix = ctx.BuildStepPrefix i
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
    )
