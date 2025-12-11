open Fsx
open System
open System.IO

let args = Environment.GetCommandLineArgs()

let entryFs =
    if args.Length > 1 then
        let file = Path.Combine(Environment.CurrentDirectory, args[1])
        let fileInfo = FileInfo file
        if fileInfo.Exists then
            fileInfo
        else
            failwithf "The file '%s' does not exist." file
    else
        failwithf "Please provide the path to the entry .fs/.fsx file as a command line argument."


let cacheDir = Path.Combine(entryFs.DirectoryName, ".fsx")
if not (Directory.Exists(cacheDir)) then
    Directory.CreateDirectory(cacheDir) |> ignore

let projectName =
    let invalidChars = Path.GetInvalidFileNameChars()
    String [|
        for c in Path.GetFileNameWithoutExtension(entryFs.FullName) do
            if invalidChars |> Array.contains c |> not then c
    |]

let projectDir = Path.Combine(cacheDir, projectName)
if not (Directory.Exists(projectDir)) then
    Directory.CreateDirectory(projectDir) |> ignore

let projectFile = Path.Combine(projectDir, $"{projectName}.fsproj")


let lockFile = Path.Combine(projectDir, "fsx.lock")

let lockFileContent =
    let entryFsLastWriteTime = entryFs.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.sss")

    let fsxLastWriteTime =
        FileInfo(Path.Combine(AppContext.BaseDirectory, "fsx.exe")).LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.sss")

    $"{entryFsLastWriteTime}|{fsxLastWriteTime}"


let argsSplitIndex = Array.tryFindIndex ((=) "--") args

let dotnetArgs =
    match argsSplitIndex with
    | Some index -> args |> Seq.skip 2 |> Seq.take (index - 2) |> Seq.toList
    | None -> args |> Seq.skip 2 |> Seq.toList

let fsArgs =
    match argsSplitIndex with
    | Some index -> args |> Seq.skip (index + 1) |> Seq.toList
    | None -> List.empty


if File.Exists(lockFile) && File.ReadAllText lockFile = lockFileContent then
    Execution.Run(projectFile, dotnetArgs, fsArgs, noRestore = true)
else
    Project.Process(entryFs.FullName, projectFile) |> ignore
    File.WriteAllText(lockFile, lockFileContent)
    Execution.Run(projectFile, dotnetArgs, fsArgs)
