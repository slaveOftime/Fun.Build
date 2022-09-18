[<AutoOpen>]
module Fun.Build.BuiltinCmds

open System
open Spectre.Console
open System.Diagnostics
open System.Runtime.InteropServices


/// Create a command with a formattable string which will encode the arguments as * when print to console.
let cmd (commandStr: FormattableString) =
    step (fun ctx -> async {
        let command = ctx.BuildCommand(commandStr.ToString())
        let args: obj[] = Array.create commandStr.ArgumentCount "*"
        let encryptiedStr = String.Format(commandStr.Format, args)

        AnsiConsole.MarkupLine $"[green]{encryptiedStr}[/]"

        return! Process.StartAsync(command, encryptiedStr)
    }
    )


/// Open url in browser
let openBrowser (url: string) =
    step (fun _ -> async {
        try
            Process.Start(url) |> ignore
            return 0
        with _ ->
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                Process.Start(ProcessStartInfo(FileName = "cmd", Arguments = $"/c start {url}", UseShellExecute = true)) |> ignore
                return 0
            else if RuntimeInformation.IsOSPlatform OSPlatform.Linux then
                Process.Start("xdg-open", url) |> ignore
                return 0
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) then
                Process.Start("open", url) |> ignore
                return 0
            else
                AnsiConsole.MarkupLine "[red]Open url failed. Platform is not supportted.[/]"
                return -1
    }
    )
