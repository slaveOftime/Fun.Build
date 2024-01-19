[<AutoOpen>]
module Fun.Build.Cli.Source

open System.IO
open Spectre.Console
open Fun.Build.Cli

let private sourcesFile = funBuildCliCacheDir </> "sources.txt"

let mutable private sources =
    try
        File.ReadAllLines sourcesFile |> Seq.toList
    with _ -> []


type Source =

    static member Sources = sources


    static member PromptRemove() =
        let selections = MultiSelectionPrompt<string>()
        selections.Title <- "Select the source directory you want to remove"
        selections.AddChoices(Source.Sources) |> ignore
        let selection = AnsiConsole.Prompt(selections)

        sources <- (sources |> Seq.filter (fun x -> selection |> Seq.contains x |> not) |> Seq.toList)

        File.WriteAllLines(sourcesFile, sources)

        if sources.Length = 0 then Source.PromptAdd()


    static member PromptAdd() =
        let source =
            AnsiConsole.MarkupLine
                "[yellow]You need to provide at least one source folder which should contains .fsx file under it or under its sub folders:[/]"
            AnsiConsole.Ask<string>("> ")
        if Directory.Exists source then
            sources <- sources @ [ source ]
            File.WriteAllLines(sourcesFile, sources)
            Pipeline.BuildCache source |> Async.RunSynchronously
            if AnsiConsole.Confirm("Do you want to add more source dir?", false) then
                Source.PromptAdd()
        else
            AnsiConsole.MarkupLine("The folder is not exist, please try again")
            Source.PromptAdd()
