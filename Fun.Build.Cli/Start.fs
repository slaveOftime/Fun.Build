open System
open System.IO
open System.Text
open Spectre.Console
open Fun.Result
open Fun.Build
open Fun.Build.Cli
open System.IO
open System.IO
open Spectre.Console


Console.InputEncoding <- Encoding.UTF8
Console.OutputEncoding <- Encoding.UTF8


// Print title
let title = FigletText("Fun Build Cli").LeftJustified()
title.Color <- Color.HotPink
AnsiConsole.Write(title)


pipeline "source" {
    description "Manage source directory"
    stage "list" {
        whenCmdArg "--list" "" "List current source directories"
        run (fun _ ->
            let table = Table().AddColumns("").HideHeaders()
            for source in Source.Sources do
                table.AddRow(source) |> ignore
            AnsiConsole.Write(table)
            Environment.Exit(0)
        )
    }
    stage "add" {
        whenCmdArg "--add" "" "Add source directory and build pipeline info cache for usage"
        run (fun _ ->
            Source.PromptAdd()
            Environment.Exit(0)
        )
    }
    stage "remove" {
        whenCmdArg "--remove" "" "Remove source from current source list"
        run (fun _ ->
            Source.PromptRemove()
            Environment.Exit(0)
        )
    }
    stage "clean" {
        whenCmdArg "--clean" "" "Clear all the files and restart again"
        run (fun _ -> Directory.Delete(funBuildCliCacheDir, recursive = true))
    }
    stage "refresh" {
        whenCmdArg "--refresh" "" "Rebuild pipelines and cache again for current source again"
        run (fun _ -> async {
            Pipeline.ClearCache()
            for source in Source.Sources do
                do! Pipeline.BuildCache source
            Environment.Exit(0)
        })
    }
    runIfOnlySpecified
}


let executionOptions = {|
    useLastRun = CmdArg.Create(longName = "--use-last-run", description = "Execute the last pipeline")
|}

pipeline "run" {
    description "Execute pipeline found from sources"
    stage "ensure-source" { run (fun _ -> if Source.Sources.Length = 0 then Source.PromptAdd()) }
    stage "auto" {
        whenCmdArg executionOptions.useLastRun
        run (fun ctx -> asyncResult {
            match History.LoadLastOne() with
            | Some history -> do! History.Run(ctx, history)
            | None ->
                AnsiConsole.MarkupLine("[yellow]No history found to run automatically[/]")
                match Pipeline.PromtSelect() with
                | ValueSome p -> do! Pipeline.Run(ctx, p)
                | _ -> do! AsyncResult.ofError "No pipelines are selected"
            Environment.Exit(0)
        })
    }
    stage "select" {
        whenNot { cmdArg executionOptions.useLastRun }
        run (fun ctx -> asyncResult {
            let selectPipelineAndRun () =
                match Pipeline.PromtSelect() with
                | ValueSome p -> Pipeline.Run(ctx, p)
                | _ -> AsyncResult.ofError "No pipelines are selected"

            if AnsiConsole.Confirm("Re-run pipeline from history?", true) then
                match History.PromptSelect() with
                | ValueSome history -> do! History.Run(ctx, history)
                | _ -> do! selectPipelineAndRun ()
            else
                do! selectPipelineAndRun ()
            Environment.Exit(0)
        })
    }
    runIfOnlySpecified false
}


tryPrintPipelineCommandHelp ()
