[<AutoOpen>]
module Fun.Build.Cli.Pipeline

open System
open System.IO
open Spectre.Console
open Fun.Result
open Fun.Build
open Fun.Build.Cli

let private pipelineInfoDir = funBuildCliCacheDir </> "pipeline-infos" |> ensureDir


type Pipeline with

    static member Parse(str: string) =
        let lines = str.Split(Environment.NewLine)

        if lines.Length = 0 then
            []

        else if lines[0].StartsWith("Description:") then
            let name = lines[1].Split(" ") |> Seq.map (fun x -> x.Trim()) |> Seq.filter (String.IsNullOrEmpty >> not) |> Seq.item 1
            let description = lines[2].Trim()
            [ name, description ]

        else
            let pipelines = Collections.Generic.List<string * string>()
            let mutable index = 0
            let mutable shouldContinue = true
            let mutable isPipeline = false

            while shouldContinue do
                let line = lines[index].Trim()
                index <- index + 1
                if line = "Pipelines:" then
                    isPipeline <- true
                else if isPipeline && not (String.IsNullOrEmpty(line)) then
                    let index = line.Trim().IndexOf(" ")
                    if index > 0 then
                        pipelines.Add(line.Substring(0, index).Trim(), line.Substring(index + 1).Trim())
                    else
                        pipelines.Add(line.Trim(), "")
                else if isPipeline then
                    shouldContinue <- false
                else
                    shouldContinue <- index < lines.Length

            pipelines |> Seq.toList


    static member ClearCache() = Directory.GetFiles(pipelineInfoDir, "*", EnumerationOptions(RecurseSubdirectories = true)) |> Seq.iter File.Delete


    static member BuildCache(dir: string, ?timeout, ?paralleCount) =
        AnsiConsole.MarkupLine($"Start to build pipeline info cache for: [green]{dir}[/]")

        let pipelineInfoDir = pipelineInfoDir </> hashString dir |> ensureDir

        let asyncs =
            Directory.GetFiles(dir, "*.fsx", EnumerationOptions(RecurseSubdirectories = true))
            |> Seq.map (fun f -> async {
                try
                    let pipelineInfoFile = pipelineInfoDir </> hashString f

                    let isValidFile = lazy (File.ReadLines(f) |> Seq.exists (fun l -> l.Contains("tryPrintPipelineCommandHelp")))

                    let isScriptChanged =
                        let pInfoFileInfo = FileInfo pipelineInfoFile
                        if pInfoFileInfo.Exists then
                            pInfoFileInfo.Length = 0 || pInfoFileInfo.LastWriteTime <> (FileInfo f).LastWriteTime
                        else
                            true

                    if isScriptChanged && isValidFile.Value then
                        printfn "Process script %s" f
                        let psInfo = Diagnostics.ProcessStartInfo()
                        psInfo.FileName <- Diagnostics.Process.GetQualifiedFileName "dotnet"
                        psInfo.Arguments <- $"fsi \"{f}\" -- -h"

                        let! result =
                            Async.StartChild(
                                Diagnostics.Process.StartAsync(psInfo, "", "", printOutput = false, captureOutput = true),
                                millisecondsTimeout = defaultArg timeout 60_000
                            )
                        let! result = result

                        let pipelineInfos = Pipeline.Parse result.StandardOutput |> Seq.map (fun (x, y) -> sprintf "%s,%s,%s" f x y)
                        File.WriteAllLines(pipelineInfoFile, pipelineInfos)
                        File.SetLastWriteTime(pipelineInfoFile, FileInfo(f).LastWriteTime)

                    else if not isScriptChanged && isValidFile.Value then
                        printfn "Script is not changed: %s" f

                    if isValidFile.Value then return Some pipelineInfoFile else return None

                with ex ->
                    AnsiConsole.MarkupLineInterpolated($"[red]Process script {f} failed: {ex.Message}[/]")
                    return None
            })
            |> fun ls -> Async.Parallel(ls, maxDegreeOfParallelism = defaultArg paralleCount 4)
            |> Async.map (Seq.choose id)

        async {
            let! files = asyncs |> Async.map (Seq.map Path.GetFileNameWithoutExtension >> Seq.toList)
            let oldFiles = Directory.GetFiles(pipelineInfoDir)
            for oldFile in oldFiles do
                if files |> Seq.contains (Path.GetFileNameWithoutExtension oldFile) |> not then
                    printfn "Removing unused pipeline info cache %s" oldFile
                    File.Delete oldFile
        }


    static member LoadAll() =
        Directory.GetFiles(pipelineInfoDir, "*", EnumerationOptions(RecurseSubdirectories = true))
        |> Seq.map (fun f ->
            File.ReadAllLines(f)
            |> Seq.map (fun l ->
                let columns = l.Split(",")
                {
                    Pipeline.Script = columns[0]
                    Name = columns[1]
                    Description = if columns.Length > 2 then columns[2] else ""
                }
            )
        )
        |> Seq.concat
        |> Seq.sortBy (fun x -> x.Script, x.Name)
        |> Seq.toList


    static member filter (query: string) (pipelines: Pipeline seq) =
        let qs = query.Split(" ") |> Seq.map (fun x -> x.Trim()) |> Seq.filter (String.IsNullOrEmpty >> not) |> Seq.toList
        pipelines
        |> Seq.filter (fun p ->
            qs
            |> Seq.forall (fun query ->
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Script.Contains(query, StringComparison.OrdinalIgnoreCase)
            )
        )


    static member PromptSelect() =
        AnsiConsole.PromptQueryAndSelection(
            "Select pipeline to run",
            Pipeline.LoadAll(),
            Pipeline.filter,
            fun pipeline -> $"[bold green]{pipeline.Name}[/]: {pipeline.Description} ({pipeline.Script})"
        )


    static member Run
        (
            ctx: Internal.StageContext,
            pipeline: Pipeline,
            ?promptForContinuation: bool,
            ?continuationCommands: Collections.Generic.List<{| Pipeline: Pipeline; Args: string |}>
        ) =
        asyncResult {
            let promtForContinuation = defaultArg promptForContinuation false
            let scriptFile = Path.GetFileName pipeline.Script
            let scriptDir = Path.GetDirectoryName pipeline.Script

            let table = Table()
            table.ShowHeaders <- false
            table.AddColumns("", "") |> ignore
            table.AddRow("Script", $"[green]{scriptFile}[/]") |> ignore
            table.AddRow("Pipeline", $"[bold green]{pipeline.Name}[/]") |> ignore
            table.AddRow("WorkingDir", $"[green]{scriptDir}[/]") |> ignore
            if String.IsNullOrEmpty(pipeline.Description.Trim()) |> not then
                table.AddRow("Description", $"[green]{pipeline.Description}[/]") |> ignore

            AnsiConsole.Write table

            let textPrompt = TextPrompt<string>("Arguments (-h for help): ")
            textPrompt.AllowEmpty <- true
            let args = AnsiConsole.Prompt textPrompt


            let currentCommands = continuationCommands |> Option.defaultWith (fun () -> Collections.Generic.List())
            currentCommands.Add({| Pipeline = pipeline; Args = args |})


            AnsiConsole.MarkupLine("")

            if promtForContinuation then
                if AnsiConsole.Confirm("Do you want to select another pipeline to continue?", defaultValue = false) then
                    match Pipeline.PromptSelect() with
                    | ValueSome item -> do! Pipeline.Run(ctx, item, continuationCommands = currentCommands, promptForContinuation = true)
                    | _ -> ()
                AnsiConsole.MarkupLine("")

            if continuationCommands.IsNone then
                for index, command in Seq.indexed currentCommands do
                    let scriptFile = Path.GetFileName command.Pipeline.Script
                    let scriptDir = Path.GetDirectoryName command.Pipeline.Script
                    let isHelpCommand = command.Args.Trim().ToLower() = "-h"

                    if not isHelpCommand then
                        History.Add
                            {
                                Pipeline = pipeline
                                Args = args
                                StartedTime = DateTime.Now
                            }

                    if index = 0 then
                        AnsiConsole.Console.MarkupLine("[green]Start executing...[/]")
                    else
                        AnsiConsole.Console.MarkupLine("[green]Continue executing...[/]")
                    AnsiConsole.Console.WriteLine()

                    try
                        do!
                            ctx.RunCommand($"dotnet fsi \"{scriptFile}\" -- -p {command.Pipeline.Name} {command.Args}", workingDir = scriptDir)
                            |> Async.map (ignore >> Ok)
                    with _ ->
                        ()

                    if isHelpCommand && currentCommands.Count = 1 then
                        do! Pipeline.Run(ctx, pipeline)
        }
