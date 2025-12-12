namespace Fun.Fsx

open System
open System.IO
open System.Threading.Tasks

type Lock =

    static member private GetScriptsLockFile(projectDir: string) = Path.Combine(projectDir, "fsx.lock")

    static member IsScriptsModified(projectDir: string) =
        try
            let lockFile = Lock.GetScriptsLockFile projectDir

            if File.Exists lockFile then
                File.ReadAllLines lockFile
                |> Array.map (fun line -> async {
                    let parts = line.Split ','
                    let lastModified = FileInfo(parts[0]).LastWriteTimeUtc
                    return lastModified.Ticks.ToString() = parts[1]
                })
                |> Async.Parallel
                |> Async.RunSynchronously
                |> Seq.exists not
            else
                true
        with _ ->
            true

    static member UpdateScriptsLock(project: Project) : Task = task {
        let files = [ project.EntryScript; yield! project.OtherScripts; Path.Combine(AppContext.BaseDirectory, "fsx.exe") ]

        let lockFile = Lock.GetScriptsLockFile(Path.GetDirectoryName project.ProjectFile)
        if File.Exists lockFile then File.Delete lockFile

        use lockStream = File.OpenWrite(lockFile)
        use lockWriter = new StreamWriter(lockStream)
        for file in files do
            let fileInfo = FileInfo file
            lockWriter.Write(fileInfo.FullName)
            lockWriter.Write(",")
            lockWriter.WriteLine(fileInfo.LastWriteTimeUtc.Ticks.ToString())
    }


    static member private GetPackagesLockFile(projectDir: string) = Path.Combine(projectDir, "fsx-packages.lock")

    static member IsPackagesModified(project: Project) =
        let lockFile = Lock.GetPackagesLockFile(Path.GetDirectoryName project.ProjectFile)

        if File.Exists lockFile then
            let lockedPackages = File.ReadAllLines lockFile

            if lockedPackages.Length <> project.Packages.Length then
                true
            else
                Seq.zip lockedPackages project.Packages
                |> Seq.exists (fun (line, pkg) ->
                    let index = line.IndexOf ','
                    not (line.AsSpan().Slice(0, index).SequenceEqual(pkg.Name) && line.AsSpan().Slice(index + 1).SequenceEqual(pkg.Version))
                )
        else
            true

    static member UpdatePackagesLock(project: Project) : Task = task {
        let lockFile = Lock.GetPackagesLockFile(Path.GetDirectoryName project.ProjectFile)
        if File.Exists lockFile then File.Delete lockFile
        use lockStream = File.OpenWrite(lockFile)
        use lockWriter = new StreamWriter(lockStream)
        for pkg in project.Packages do
            lockWriter.Write(pkg.Name)
            lockWriter.Write(",")
            lockWriter.WriteLine(pkg.Version)
    }


    static member private GetScriptArgsLockFile(projectDir: string) = Path.Combine(projectDir, "fsx-args.lock")

    static member IsScriptArgsModified(projectDir: string, scriptArgs: string seq) =
        let lockFile = Lock.GetScriptArgsLockFile projectDir

        if File.Exists lockFile then
            let lockedArgs = File.ReadAllLines lockFile |> Array.toList
            if lockedArgs.Length <> Seq.length scriptArgs then
                true
            else
                Seq.zip lockedArgs scriptArgs |> Seq.exists (fun (locked, arg) -> locked <> arg)
        else
            true

    static member UpdateScriptArgsLock(projectDir: string, scriptArgs: string list) : Task = task {
        let lockFile = Lock.GetScriptArgsLockFile projectDir
        if File.Exists lockFile then File.Delete lockFile
        use lockStream = File.OpenWrite(lockFile)
        use lockWriter = new StreamWriter(lockStream)
        for arg in scriptArgs do
            lockWriter.WriteLine(arg)
    }
