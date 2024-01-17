open System
open System.IO
open System.Text
open System.Security.Cryptography
open Spectre.Console
open Fun.Result
open Fun.Build


Console.InputEncoding <- Encoding.UTF8
Console.OutputEncoding <- Encoding.UTF8


let (</>) x y = Path.Combine(x, y)

let ensureDir x =
    if Directory.Exists x |> not then Directory.CreateDirectory x |> ignore
    x

let funBuildCliCacheDir =
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) </> "fun-build" |> ensureDir
let pipelineInfoDir = funBuildCliCacheDir </> "pipeline-infos" |> ensureDir
let sourcesFile = funBuildCliCacheDir </> "sources.txt"
let historyFile = funBuildCliCacheDir </> "history.csv"


type Pipeline = { Script: string; Name: string; Description: string }

type ExecutionHistoryItem = {
    Script: string
    Pipeline: string
    Args: string
    StartedTime: DateTime
}


let mutable sources =
    try
        File.ReadAllLines sourcesFile |> Seq.toList
    with _ -> []


let parseHistory (line: string) =
    let columns = line.Split ","
    {
        Script = columns[0]
        Pipeline = columns[1]
        Args = columns[2]
        StartedTime = DateTime.Parse(columns[3])
    }


let addHistory (item: ExecutionHistoryItem) = File.AppendAllLines(historyFile, [ $"{item.Script},{item.Pipeline},{item.Args},{item.StartedTime}" ])


let parsePipelines (str: string) =
    let pipelines = Collections.Generic.List<string * string>()
    let lines = str.Split(Environment.NewLine)
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
                pipelines.Add(line.Substring(0, index), line.Substring(index + 1).Trim())
            else
                pipelines.Add(line, "")
        else if isPipeline then
            shouldContinue <- false
        else
            shouldContinue <- index < lines.Length

    pipelines |> Seq.toList


let hashString (str: string) = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(str)))


let refreshPipelineInfos (dir: string) =
    Directory.GetFiles(dir, "*.fsx", EnumerationOptions(RecurseSubdirectories = true))
    |> Seq.map (fun f -> async {
        try
            let isValidFile = File.ReadLines(f) |> Seq.exists (fun l -> l.Contains("tryPrintPipelineCommandHelp"))
            if isValidFile then
                printfn "Process script %s" f
                let psInfo = Diagnostics.ProcessStartInfo()
                psInfo.FileName <- Diagnostics.Process.GetQualifiedFileName "dotnet"
                psInfo.Arguments <- $"fsi \"{f}\" -- -h"
                let! result = Diagnostics.Process.StartAsync(psInfo, "", "", printOutput = false, captureOutput = true)
                let pipelineInfoFile = pipelineInfoDir </> hashString f
                let pipelineInfos = parsePipelines result.StandardOutput |> Seq.map (fun (x, y) -> sprintf "%s,%s,%s" f x y)
                File.WriteAllLines(pipelineInfoFile, pipelineInfos)

        with ex ->
            AnsiConsole.MarkupLineInterpolated($"[red]Process script {f} failed: {ex.Message}[/]")
    })
    |> Async.Parallel
    |> Async.map ignore


let rec addSourceDir () =
    let source =
        AnsiConsole.Ask<string>("You need to provide at least one source folder which should contains .fsx file under it or under its sub folders:")
    if Directory.Exists source then
        sources <- sources @ [ source ]
        File.WriteAllLines(sourcesFile, sources)
        refreshPipelineInfos source |> Async.RunSynchronously
        if AnsiConsole.Confirm("Do you want to add more source dir?", false) then
            addSourceDir ()
    else
        AnsiConsole.MarkupLine("The folder is not exist, please try again:")
        addSourceDir ()


let selectHistory () =
    let historyLines =
        try
            File.ReadLines(historyFile)
        with _ -> [||]

    let count = historyLines |> Seq.length
    let limit = 10
    let skipCount = if count > limit then count - limit else 0
    let histories = historyLines |> Seq.skip skipCount |> Seq.map parseHistory |> Seq.rev |> Seq.toList

    if histories.IsEmpty then
        AnsiConsole.MarkupLine("[yellow]No history found[/]")
        None
    else
        let selection = SelectionPrompt()
        selection.Title <- "Select history to re-run"
        selection.Converter <- snd
        selection.AddChoices [
            for i, history in Seq.indexed histories do
                let time = history.StartedTime.ToString("yyyy-MM-dd HH:mm:ss")
                i, $"{time}: {history.Pipeline} {history.Args} {history.Script}"
        ]
        |> ignore
        let index, _ = AnsiConsole.Prompt(selection)
        Seq.tryItem index histories

let rec runPipeline (ctx: Internal.StageContext) (pipeline: Pipeline) = asyncResult {
    AnsiConsole.MarkupLineInterpolated($"Pipeline: [bold green]{pipeline.Name}[/]")
    if String.IsNullOrEmpty(pipeline.Description.Trim()) |> not then
        AnsiConsole.MarkupLineInterpolated($"[green]{pipeline.Description}[/]")
    AnsiConsole.MarkupLineInterpolated($"Script: [green]{pipeline.Script}[/]")

    let scriptFile = Path.GetFileName pipeline.Script
    let scriptDir = Path.GetDirectoryName pipeline.Script
    let args = AnsiConsole.Ask<string>("Arguments: ")

    AnsiConsole.MarkupLineInterpolated($"Working dir: [green]{scriptDir}[/]")

    let startedTime = DateTime.Now
    do!
        ctx.RunCommand($"dotnet fsi \"{scriptFile}\" -- -p {pipeline.Name} {args}", workingDir = scriptDir)
        |> Async.map (ignore >> Ok)

    if args.Trim() <> "-h" then
        addHistory
            {
                Script = pipeline.Script
                Pipeline = pipeline.Name
                Args = args
                StartedTime = startedTime
            }
    else
        do! runPipeline ctx pipeline
}

let reRunHistory (ctx: Internal.StageContext) (history: ExecutionHistoryItem) = asyncResult {
    let args =
        let newArgs = AnsiConsole.Ask<string>("Type new arguments or press ENTER to use the one from history", "")
        if String.IsNullOrEmpty newArgs then history.Args else newArgs

    let scriptFile = Path.GetFileName history.Script
    let scriptDir = Path.GetDirectoryName history.Script

    AnsiConsole.MarkupLineInterpolated($"[green]Working dir: {scriptDir}[/]")

    let startTime = DateTime.Now
    do!
        ctx.RunCommand($"dotnet fsi \"{scriptFile}\" -- -p {history.Pipeline} {args}", workingDir = scriptDir)
        |> Async.map (ignore >> Ok)
    addHistory { history with StartedTime = startTime }
}


let selectPipelineToRun (ctx: Internal.StageContext) = asyncResult {
    let pipelines =
        Directory.GetFiles(pipelineInfoDir)
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

    let selectAndRunPipelines (pipelines: Pipeline seq) = asyncResult {
        let selection = SelectionPrompt<Pipeline>()
        selection.Title <- "Select pipeline to run"
        selection.Converter <- fun p -> $"[bold green]{p.Name}[/]: {p.Description} ({p.Script})"
        selection.AddChoices(pipelines |> Seq.sortBy (fun x -> x.Script, x.Name)) |> ignore
        let selectedPipeline = AnsiConsole.Prompt selection
        do! runPipeline ctx selectedPipeline
    }

    if AnsiConsole.Confirm("Do you want to search pipeline by query (y) or select manually (n)?", true) then
        let rec queryAndRunPipeline () = asyncResult {
            let query = AnsiConsole.Ask<string>("Query by script file name or pipeline info: ")
            let filteredPipelines =
                pipelines
                |> Seq.filter (fun p ->
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.Script.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                |> Seq.toList

            match filteredPipelines with
            | [] ->
                AnsiConsole.MarkupLine("[yellow]No pipelines are found[/]")
                do! queryAndRunPipeline ()
            | [ pipeline ] -> do! runPipeline ctx pipeline
            | ps -> do! selectAndRunPipelines ps
        }

        do! queryAndRunPipeline ()

    else
        do! selectAndRunPipelines pipelines

}


// Print title
let title = FigletText("Fun Build Cli").LeftJustified()
title.Color <- Color.HotPink
AnsiConsole.Write(title)

// Ensure to have at least one source dir
if sources.Length = 0 then addSourceDir ()


pipeline "source" {
    description "Manage source directory"
    stage "add" {
        whenCmdArg "--add"
        run (ignore >> addSourceDir)
    }
    stage "remove" {
        whenCmdArg "--remove"
        run (fun _ ->
            let selections = MultiSelectionPrompt<string>()
            selections.Title <- "Select the source directory you want to remove"
            selections.AddChoices(sources) |> ignore
            let choices = AnsiConsole.Prompt(selections)
            sources <- sources |> Seq.filter (fun x -> choices |> Seq.contains x |> not) |> Seq.toList
            File.WriteAllLines(sourcesFile, sources)
            if sources.Length = 0 then addSourceDir ()
        )
    }
    stage "refresh" {
        whenCmdArg "--refresh" "" "Refresh source pipelines and cache again"
        run (fun _ -> async {
            for source in sources do
                do! refreshPipelineInfos source
        })
    }
    runIfOnlySpecified
}


let executionOptions = {|
    useLastRun = CmdArg.Create(longName = "--use-last-run", description = "Execute the last pipeline")
|}

pipeline "execution" {
    description "Execute pipeline found from sources"
    stage "auto" {
        whenCmdArg executionOptions.useLastRun
        run (fun ctx -> asyncResult {
            let history = File.ReadLines(historyFile) |> Seq.tryLast |> Option.map parseHistory
            match history with
            | Some history -> do! reRunHistory ctx history
            | None -> AnsiConsole.MarkupLine("[yellow]No history found to run automatically[/]")
        })
    }
    stage "select" {
        whenNot { cmdArg executionOptions.useLastRun }
        run (fun ctx -> asyncResult {
            if AnsiConsole.Confirm("Do you want to select history to re-run?", true) then
                match selectHistory () with
                | Some history -> do! reRunHistory ctx history
                | None -> do! selectPipelineToRun ctx
            else
                do! selectPipelineToRun ctx
        })
    }
    runIfOnlySpecified false
}


tryPrintPipelineCommandHelp ()
