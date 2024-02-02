namespace Fun.Build.Cli

open System
open System.IO
open Spectre.Console
open Fun.Result
open Fun.Build

type History =

    static member private File(i: int) = funBuildCliCacheDir </> $"history{i}.csv"

    static member Parse(line: string) =
        let columns = line.Split ","
        {
            Args = columns[2]
            StartedTime = DateTime.Parse(columns[3])
            Pipeline = {
                Script = columns[0]
                Name = columns[1]
                Description = if columns.Length > 3 then columns[4] else ""
            }
        }


    static member Add(item: HistoryItem) =
        let lines =
            try
                File.ReadLines(History.File 0) |> Seq.length
            with _ ->
                0

        if lines > 200 then File.Move(History.File 0, History.File 1, overwrite = true)

        File.AppendAllLines(
            History.File 0,
            [ $"{item.Pipeline.Script},{item.Pipeline.Name},{item.Args},{item.StartedTime},{item.Pipeline.Description}" ]
        )

    static member Clear() =
        for i in 0..1 do
            try
                File.Delete(History.File i)
            with _ ->
                ()


    static member LoadAll() = [
        for i in 1 .. (-1) .. 0 do
            try
                yield! File.ReadAllLines(History.File i) |> Seq.map History.Parse
            with _ ->
                ()
    ]


    static member LoadLastOne() =
        try
            File.ReadLines(History.File 0) |> Seq.tryLast |> Option.map History.Parse
        with _ ->
            None


    static member filter (query: string) (pipelines: HistoryItem seq) =
        let qs = query.Split(" ") |> Seq.map (fun x -> x.Trim()) |> Seq.filter (String.IsNullOrEmpty >> not) |> Seq.toList
        pipelines
        |> Seq.filter (fun p ->
            qs
            |> Seq.forall (fun query ->
                p.Pipeline.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Pipeline.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Pipeline.Script.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Args.Contains(query, StringComparison.OrdinalIgnoreCase)
            )
        )


    static member PromptSelect() =
        let historyLines = History.LoadAll()
        let histories = historyLines |> Seq.rev |> Seq.toList

        if histories.IsEmpty then
            AnsiConsole.MarkupLine("[yellow]No history found[/]")
            ValueNone

        else
            AnsiConsole.PromptQueryAndSelection(
                "Select history to re-run",
                histories,
                History.filter,
                fun history ->
                    let time = history.StartedTime.ToString("MM-dd HH:mm:ss")
                    $"{time}: [green]-p {history.Pipeline.Name} {history.Args}[/] [grey]({history.Pipeline.Script})[/]"
            )


    static member Run(ctx: Internal.StageContext, history: HistoryItem, ?withLastArgs: bool) = asyncResult {
        let withLastArgs = defaultArg withLastArgs false
        let scriptFile = Path.GetFileName history.Pipeline.Script
        let scriptDir = Path.GetDirectoryName history.Pipeline.Script

        let table = Table()
        table.ShowHeaders <- false
        table.AddColumns("", "") |> ignore
        table.AddRow("Script", $"[green]{scriptFile}[/]") |> ignore
        table.AddRow("Pipeline", $"[bold green]{history.Pipeline.Name}[/]") |> ignore
        table.AddRow("WorkingDir", $"[green]{scriptDir}[/]") |> ignore
        table.AddRow("Arguments", history.Args) |> ignore

        AnsiConsole.Write table
        AnsiConsole.MarkupLine("New arguments (leave empty to use the one from history):")

        let args =
            if withLastArgs then
                history.Args
            else
                let text = TextPrompt<string>("> ")
                text.AllowEmpty <- true
                let newArgs = AnsiConsole.Prompt text
                if String.IsNullOrEmpty newArgs then history.Args else newArgs

        let isHelpCommand = args = "-h"

        if not isHelpCommand then
            History.Add { history with Args = args; StartedTime = DateTime.Now }

        AnsiConsole.MarkupLine("")

        do!
            ctx.RunCommand($"dotnet fsi \"{scriptFile}\" -- -p {history.Pipeline.Name} {args}", workingDir = scriptDir)
            |> Async.map (ignore >> Ok)

        if isHelpCommand then do! History.Run(ctx, history)
    }
