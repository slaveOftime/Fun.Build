[<AutoOpen>]
module Fun.Build.Cli.AnsiConsole

open System
open System.Linq
open Spectre.Console
open Spectre.Console

type AnsiConsole with

    static member PromptQueryAndSelection<'T>(selectionTitle: string, items: 'T seq, filter: string -> 'T seq -> 'T seq, converter: 'T -> string) =
        let mutable query = ""
        let mutable startSelect = false
        let mutable selectedItem = ValueOption<'T>.None
        let mutable shouldContinue = true

        Console.PushCursorToTop()

        while shouldContinue do
            Console.ClearScreen()

            let filteredPipelines = items |> filter query |> Seq.toList

            if startSelect then
                let selections = SelectionPrompt<'T>()
                selections.Title <- selectionTitle
                selections.Converter <- converter
                selections.AddChoices(filteredPipelines) |> ignore
                let selection = AnsiConsole.Prompt(selections)
                shouldContinue <- false
                selectedItem <- ValueSome selection

            else
                AnsiConsole.MarkupLineInterpolated($"[green]{selectionTitle}[/]:")
                for i, pipeline in filteredPipelines.Take(5) |> Seq.indexed do
                    let prefix = if i = 0 then "> " else "  "
                    AnsiConsole.Markup($"[green]{prefix}[/]")
                    AnsiConsole.MarkupLine(converter pipeline)

                if filteredPipelines.Length > 5 then
                    AnsiConsole.MarkupLine($"  ... {filteredPipelines.Length - 5} MORE")
                if filteredPipelines.IsEmpty then
                    AnsiConsole.MarkupLine("[yellow]No records are found[/]")

                AnsiConsole.MarkupLine("")
                AnsiConsole.MarkupLine("Search (space is query spliter) or select by keyboard (ArrowDown for selecting, Enter for the first one): ")
                AnsiConsole.Markup($"> {query}")
                let k = Console.ReadKey()

                if k.Key = ConsoleKey.Enter then
                    let firstPipeline = filteredPipelines |> Seq.tryHead
                    match firstPipeline with
                    | Some p ->
                        shouldContinue <- false
                        selectedItem <- ValueSome p
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

        selectedItem
