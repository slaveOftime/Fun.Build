open System
open System.IO
open System.Threading.Tasks
open Fun.Fsx

let args = Environment.GetCommandLineArgs()

if args.Length < 2 || args[1] = "--help" || args[1] = "-h" then
    Execution.PrintHelp()
    Environment.Exit(0)
else
    let stopwatch = Diagnostics.Stopwatch.StartNew()

    let isDiagnostic =
        let vIndex =
            match Seq.tryFindIndex ((=) "-v") args with
            | None -> Seq.tryFindIndex ((=) "-verbosity") args
            | x -> x
        match vIndex with
        | Some index when index + 1 < args.Length ->
            match args[index + 1] with
            | "diag"
            | "diagnostic" -> true
            | _ -> false
        | _ -> false

    let printTimeCost (msg: string) =
        if isDiagnostic then
            Console.WriteLine("---FSX {0} cost {1}ms", msg, stopwatch.ElapsedMilliseconds)
            stopwatch.Restart()

    let supportedCommands = [ "run"; "build"; "pack" ]

    let command, commandArgs, scriptArgs =
        match Seq.contains args[1] supportedCommands, Array.tryFindIndex ((=) "--") args with
        | true, Some index -> ValueSome args[1], args |> Seq.skip 3 |> Seq.take (index - 3) |> Seq.toList, args |> Seq.skip (index + 1) |> Seq.toList
        | true, None -> ValueSome args[1], args |> Seq.skip 3 |> Seq.toList, []
        | false, Some index -> ValueNone, args |> Seq.skip 2 |> Seq.take (index - 2) |> Seq.toList, args |> Seq.skip (index + 1) |> Seq.toList
        | false, None -> ValueNone, args |> Seq.skip 2 |> Seq.toList, []

    let entryScriptArg = if command.IsSome then args[2] else args[1]
    let entryScriptFullPath = Path.Combine(Environment.CurrentDirectory, entryScriptArg)
    let entryScriptFileInfo = FileInfo entryScriptFullPath
    if not entryScriptFileInfo.Exists then
        failwithf "The file '%s' does not exist." entryScriptFullPath

    let scriptArgs = [ entryScriptArg; yield! scriptArgs ]


    let cacheDir = Path.Combine(entryScriptFileInfo.DirectoryName, ".fsx")
    if not (Directory.Exists(cacheDir)) then
        Directory.CreateDirectory(cacheDir) |> ignore

    let projectName =
        let invalidChars = Path.GetInvalidFileNameChars()
        String [|
            for c in Path.GetFileNameWithoutExtension(entryScriptFileInfo.FullName) do
                if invalidChars |> Array.contains c |> not then c
        |]

    let projectDir = Path.Combine(cacheDir, projectName)
    if not (Directory.Exists(projectDir)) then
        Directory.CreateDirectory(projectDir) |> ignore

    let projectFile = Path.Combine(projectDir, $"{projectName}.fsproj") |> Path.GetFullPath

    printTimeCost "Prepare"

    if Lock.IsScriptsModified projectDir then
        let project = Project.Parse(entryScriptFileInfo.FullName, projectFile, scriptArgs)
        printTimeCost "Parse"

        project.CreateOrUpdate()
        let isPackagesModified = Lock.IsPackagesModified project
        let _ = Task.Run(fun () -> Lock.UpdateScriptsLock project)
        let _ = Task.Run(fun () -> Lock.UpdatePackagesLock project)
        printTimeCost "Create or Update Project"

        Execution.Run(
            entryScriptFileInfo.DirectoryName,
            projectFile,
            command,
            commandArgs,
            scriptArgs,
            noRestore = (project.Packages.IsEmpty || not isPackagesModified),
            printTimeCost = printTimeCost
        )

    else
        Execution.Run(
            entryScriptFileInfo.DirectoryName,
            projectFile,
            command,
            commandArgs,
            scriptArgs,
            noRestore = true,
            noBuild = true,
            printTimeCost = printTimeCost
        )
