[<AutoOpen>]
module Fun.Build.Cli.Core

open System
open System.IO
open System.Text
open System.Security.Cryptography

let (</>) x y = Path.Combine(x, y)

let ensureDir x =
    if Directory.Exists x |> not then Directory.CreateDirectory x |> ignore
    x

let funBuildCliCacheDir =
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) </> "Fun.Build.Cli" |> ensureDir

let hashString (str: string) = Guid(MD5.HashData(Encoding.UTF8.GetBytes(str))).ToString()


type Pipeline = { Script: string; Name: string; Description: string }

type HistoryItem = { Args: string; StartedTime: DateTime; Pipeline: Pipeline }


type Console with

    /// Fill rest of screen with empty line, so we can reset the cursor to top
    static member PushCursorToTop() =
        let top = Console.CursorTop
        for _ in Console.WindowHeight - top - 1 .. Console.WindowHeight - 1 do
            Console.WriteLine(String(' ', Console.WindowWidth))
        Console.SetCursorPosition(0, 0)

    // Clear last render
    static member ClearScreen() =
        for i in 0 .. Console.WindowHeight - 1 do
            Console.SetCursorPosition(0, i)
            Console.Write(String(' ', Console.WindowWidth))
        Console.SetCursorPosition(0, 0)
