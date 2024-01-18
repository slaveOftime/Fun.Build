open System
open System.IO
open System.Text
open System.Linq
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
let historyFile (i: int) = funBuildCliCacheDir </> $"history{i}.csv"


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


let addHistory (item: ExecutionHistoryItem) =
    let lines =
        try
            File.ReadLines(historyFile 0) |> Seq.length
        with _ ->
            0

    if lines > 100 then File.Move(historyFile 0, historyFile 1, overwrite = true)

    File.AppendAllLines(historyFile 0, [ $"{item.Script},{item.Pipeline},{item.Args},{item.StartedTime}" ])

let getAllHistory () = [
    try
        yield! File.ReadAllLines(historyFile 1) |> Seq.map parseHistory
    with _ ->
        ()
    try
        yield! File.ReadAllLines(historyFile 0) |> Seq.map parseHistory
    with _ ->
        ()
]

let getLastHistory () =
    try
        File.ReadLines(historyFile 0) |> Seq.tryLast |> Option.map parseHistory
    with _ ->
        None

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


let hashString (str: string) = Guid(MD5.HashData(Encoding.UTF8.GetBytes(str))).ToString()


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

let getAllPipelines () =
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
    |> Seq.sortBy (fun x -> x.Script, x.Name)
    |> Seq.toList

let filterPipelines (query: string) (pipelines: Pipeline seq) =
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
    let historyLines = getAllHistory ()

    let count = historyLines |> Seq.length
    let limit = 10
    let skipCount = if count > limit then count - limit else 0
    let histories = historyLines |> Seq.skip skipCount |> Seq.rev |> Seq.toList

    if histories.IsEmpty then
        AnsiConsole.MarkupLine("[yellow]No history found[/]")
        None
    else
        let selection = SelectionPrompt()
        selection.Title <- "Select history to re-run"
        selection.Converter <- snd
        selection.AddChoices [
            for i, history in Seq.indexed histories do
                let time = history.StartedTime.ToString("MM-dd HH:mm:ss")
                i, $"{time}: [green]-p {history.Pipeline} {history.Args}[/] [grey]({history.Script})[/]"
        ]
        |> ignore
        let index, _ = AnsiConsole.Prompt(selection)
        Seq.tryItem index histories

let rec runPipeline (ctx: Internal.StageContext) (pipeline: Pipeline) = asyncResult {
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

    let args = AnsiConsole.Ask<string>("Arguments (-h for help): ")
    let isHelpCommand = args.Trim() <> "-h"

    if isHelpCommand then
        addHistory
            {
                Script = pipeline.Script
                Pipeline = pipeline.Name
                Args = args
                StartedTime = DateTime.Now
            }

    AnsiConsole.MarkupLine("")

    do!
        ctx.RunCommand($"dotnet fsi \"{scriptFile}\" -- -p {pipeline.Name} {args}", workingDir = scriptDir)
        |> Async.map (ignore >> Ok)

    if not isHelpCommand then do! runPipeline ctx pipeline
}

let rec reRunHistory (ctx: Internal.StageContext) (history: ExecutionHistoryItem) = asyncResult {
    let scriptFile = Path.GetFileName history.Script
    let scriptDir = Path.GetDirectoryName history.Script

    let table = Table()
    table.ShowHeaders <- false
    table.AddColumns("", "") |> ignore
    table.AddRow("Script", $"[green]{scriptFile}[/]") |> ignore
    table.AddRow("Pipeline", $"[bold green]{history.Pipeline}[/]") |> ignore
    table.AddRow("WorkingDir", $"[green]{scriptDir}[/]") |> ignore
    table.AddRow("Arguments", history.Args) |> ignore

    AnsiConsole.Write table

    let args =
        let text = TextPrompt<string>("New arguments (leave empty to use the one from history): ")
        text.AllowEmpty <- true
        let newArgs = AnsiConsole.Prompt text
        if String.IsNullOrEmpty newArgs then history.Args else newArgs

    let isHelpCommand = args = "-h"

    if not isHelpCommand then addHistory { history with StartedTime = DateTime.Now }

    AnsiConsole.MarkupLine("")

    do!
        ctx.RunCommand($"dotnet fsi \"{scriptFile}\" -- -p {history.Pipeline} {args}", workingDir = scriptDir)
        |> Async.map (ignore >> Ok)

    if isHelpCommand then do! reRunHistory ctx history
}

let selectPipelineToRun (ctx: Internal.StageContext) = asyncResult {
    let convertFn = fun (p: Pipeline) -> $"[bold green]{p.Name}[/]: {p.Description} ({p.Script})"

    let selectAndRunPipelines (pipelines: Pipeline seq) = asyncResult {
        let selection = SelectionPrompt<Pipeline>()
        selection.Title <- "Select pipeline to run"
        selection.Converter <- convertFn
        selection.AddChoices(pipelines |> Seq.sortBy (fun x -> x.Script, x.Name)) |> ignore
        let selectedPipeline = AnsiConsole.Prompt selection
        do! runPipeline ctx selectedPipeline
    }

    let pipelines = getAllPipelines ()

    let mutable query = ""
    let mutable shouldContinue = true
    let mutable startSelect = false

    /// Fill rest of screen with empty line, so we can reset the cursor to top
    let top = Console.CursorTop
    for _ in Console.WindowHeight - top - 1 .. Console.WindowHeight - 1 do
        Console.WriteLine(String(' ', Console.WindowWidth))
    Console.SetCursorPosition(0, 0)

    while shouldContinue do
        // Clear last render
        for i in 0 .. Console.WindowHeight - 1 do
            Console.SetCursorPosition(0, i)
            Console.Write(String(' ', Console.WindowWidth))
        Console.SetCursorPosition(0, 0)

        if startSelect then
            do! filterPipelines query pipelines |> selectAndRunPipelines

        else
            let filteredPipelines = filterPipelines query pipelines |> Seq.toList

            for i, pipeline in filteredPipelines.Take(5) |> Seq.indexed do
                let prefix = if i = 0 then "> " else "  "
                AnsiConsole.Markup($"[green]{prefix}[/]")
                AnsiConsole.MarkupLine(convertFn pipeline)

            if filteredPipelines.Length > 5 then AnsiConsole.MarkupLine("  ...")
            if filteredPipelines.IsEmpty then
                AnsiConsole.MarkupLine("[yellow]No pipelines are found[/]")

            AnsiConsole.MarkupLine("")
            AnsiConsole.MarkupLine("Search (space is query spliter) and select pipeline (ArrowDown for selecting, Enter for the first one): ")
            AnsiConsole.Markup($"> {query}")
            let k = Console.ReadKey()

            if k.Key = ConsoleKey.Enter then
                let firstPipeline = filteredPipelines |> Seq.tryHead
                match firstPipeline with
                | Some p ->
                    shouldContinue <- false
                    do! runPipeline ctx p
                | _ -> ()

            else if k.Key = ConsoleKey.DownArrow then
                startSelect <- true

            else if k.Key = ConsoleKey.Delete || k.Key = ConsoleKey.Backspace then
                query <- if query.Length > 0 then query.Substring(0, query.Length - 1) else query

            else if
                (k.KeyChar >= '0' && k.KeyChar <= '9')
                || (k.KeyChar >= 'a' && k.KeyChar <= 'z')
                || (k.KeyChar >= 'A' && k.KeyChar <= 'Z')
                || k.KeyChar = ' '
                || k.KeyChar = '.'
            then
                query <- query + string k.KeyChar
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
            Directory.GetFiles(pipelineInfoDir) |> Seq.iter File.Delete
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
            match getLastHistory () with
            | Some history -> do! reRunHistory ctx history
            | None -> AnsiConsole.MarkupLine("[yellow]No history found to run automatically[/]")
        })
    }
    stage "select" {
        whenNot { cmdArg executionOptions.useLastRun }
        run (fun ctx -> asyncResult {
            if AnsiConsole.Confirm("Re-run pipeline from history?", true) then
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
