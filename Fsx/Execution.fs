namespace Fsx

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


    static member Run(projectFile: string, dotnetArgs: string seq, fsArgs: string seq, [<Struct>] ?noRestore) =
        let proc = ProcessStartInfo(Execution.GetQualifiedFileName "dotnet")
        proc.WorkingDirectory <- Path.GetDirectoryName projectFile

        let args = [
            "run"
            if defaultValueArg noRestore false then
                "--no-restore"
                "--no-build"
            "--project"
            projectFile
            yield! dotnetArgs
            if Seq.isEmpty fsArgs |> not then
                "--"
                yield! fsArgs
        ]

        for arg in args do
            proc.ArgumentList.Add(arg)

        Process.Start(proc).WaitForExit()

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
