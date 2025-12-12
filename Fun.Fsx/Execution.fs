namespace Fun.Fsx

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices

type Execution =

    static let windowsExeExts = [ "exe"; "cmd"; "bat" ]

    static let windowsEnvPaths =
        lazy
            (fun () ->
                let envPath = Environment.GetEnvironmentVariable("PATH")
                if String.IsNullOrEmpty envPath |> not then
                    envPath.Split Path.PathSeparator |> Seq.toList
                else
                    []
            )


    static member Run
        (
            scriptDir: string,
            projectFile: string,
            command: string voption,
            commandArgs: string seq,
            scriptArgs: string seq,
            [<Struct>] ?noRestore,
            [<Struct>] ?noBuild,
            [<Struct>] ?printTimeCost: string -> unit
        ) =
        let printTimeCost = defaultValueArg printTimeCost (fun _ -> ())
        let projectDir = Path.GetDirectoryName projectFile

        let proc = ProcessStartInfo("dotnet")
        proc.WorkingDirectory <- scriptDir

        let cmd = defaultValueArg command "run"

        let args = seq {
            cmd

            if cmd = "run" then "--project"
            projectFile

            yield! commandArgs

            if cmd = "run" then
                if
                    defaultValueArg noRestore false
                    && not (Seq.contains "--no-restore" commandArgs)
                    && File.Exists(Path.Combine(projectDir, "obj", "project.assets.json"))
                then
                    "--no-restore"
                if defaultValueArg noBuild false && not (Seq.contains "--no-build" commandArgs) then
                    "--no-build"
                if not (Seq.contains "-v" commandArgs) && not (Seq.contains "--verbosity" commandArgs) then
                    "-v"
                    "q"
                if scriptArgs |> Seq.isEmpty |> not then
                    "--"
                    yield! scriptArgs
        }

        for arg in args do
            proc.ArgumentList.Add(arg)

        printTimeCost "Prepare process"
        Process.Start(proc).WaitForExit()
        printTimeCost "Execute process"


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


    static member PrintHelp() =
        printfn "Usage: fsx [command] [path-to-the-script] [command-options] -- [arguments]"
        printfn ""
        printfn "Execute fsharp scripts: .fs/.fsx"
        printfn ""
        printfn "Examples:"
        printfn "  fsx demo.fsx"
        printfn "  fsx demo.fsx -- arg1 arg2"
        printfn "  fsx run demo.fsx -- arg1 arg2"
        printfn "  fsx build demo.fsx -- arg1 arg2"
        printfn ""
        printfn "Commands:"
        printfn "  run       Run the specified script (default command)"
        printfn "  build     Build the specified script into an executable"
        printfn "  pack      Pack the specified script into a NuGet package"
        printfn ""
        printfn "command-options:"
        printfn "  should be same as 'dotnet <command> --help' output"
