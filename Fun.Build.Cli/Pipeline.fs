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
                    pipelines.Add(line.Substring(0, index).Trim(), line.Substring(index + 1).Trim())
                else
                    pipelines.Add(line.Trim(), "")
            else if isPipeline then
                shouldContinue <- false
            else
                shouldContinue <- index < lines.Length

        pipelines |> Seq.toList


    static member ClearCache() = Directory.GetFiles(pipelineInfoDir) |> Seq.iter File.Delete


    static member BuildCache(dir: string) =
        AnsiConsole.MarkupLine($"Start to build pipeline info cache for: [green]{dir}[/]")

        let asyncs =
            Directory.GetFiles(dir, "*.fsx", EnumerationOptions(RecurseSubdirectories = true))
            |> Seq.map (fun f -> async {
                try
                    let pipelineInfoFile = pipelineInfoDir </> hashString f

                    let isValidFile = lazy (File.ReadLines(f) |> Seq.exists (fun l -> l.Contains("tryPrintPipelineCommandHelp")))

                    let isScriptChanged () =
                        let pInfoFileInfo = FileInfo pipelineInfoFile
                        if pInfoFileInfo.Exists then
                            pInfoFileInfo.LastWriteTime <> (FileInfo f).LastWriteTime
                        else
                            true

                    if isScriptChanged () && isValidFile.Value then
                        printfn "Process script %s" f
                        let psInfo = Diagnostics.ProcessStartInfo()
                        psInfo.FileName <- Diagnostics.Process.GetQualifiedFileName "dotnet"
                        psInfo.Arguments <- $"fsi \"{f}\" -- -h"
                        let! result = Diagnostics.Process.StartAsync(psInfo, "", "", printOutput = false, captureOutput = true)
                        let pipelineInfos = Pipeline.Parse result.StandardOutput |> Seq.map (fun (x, y) -> sprintf "%s,%s,%s" f x y)
                        File.WriteAllLines(pipelineInfoFile, pipelineInfos)
                        File.SetLastWriteTime(pipelineInfoFile, FileInfo(f).LastWriteTime)

                    if isValidFile.Value then return Some pipelineInfoFile else return None

                with ex ->
                    AnsiConsole.MarkupLineInterpolated($"[red]Process script {f} failed: {ex.Message}[/]")
                    return None
            })
            |> Async.Parallel
            |> Async.map (Seq.choose id)

        async {
            let! files = asyncs |> Async.map (Seq.map Path.GetFileNameWithoutExtension)
            let allFiles = Directory.GetFiles(pipelineInfoDir)
            for oldFile in allFiles do
                if Seq.contains (Path.GetFileNameWithoutExtension oldFile) files |> not then
                    printfn "Removing unused pipeline info cache %s" oldFile
                    File.Delete oldFile
        }


    static member LoadAll() =
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


    static member PromtSelect() =
        AnsiConsole.PromptQueryAndSelection(
            "Select pipeline to run",
            Pipeline.LoadAll(),
            Pipeline.filter,
            fun pipeline -> $"[bold green]{pipeline.Name}[/]: {pipeline.Description} ({pipeline.Script})"
        )


    static member Run(ctx: Internal.StageContext, pipeline: Pipeline) = asyncResult {
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
        let isHelpCommand = args.Trim() = "-h"

        if not isHelpCommand then
            History.Add
                {
                    Pipeline = pipeline
                    Args = args
                    StartedTime = DateTime.Now
                }

        AnsiConsole.MarkupLine("")

        do!
            ctx.RunCommand($"dotnet fsi \"{scriptFile}\" -- -p {pipeline.Name} {args}", workingDir = scriptDir)
            |> Async.map (ignore >> Ok)

        if isHelpCommand then do! Pipeline.Run(ctx, pipeline)
    }
