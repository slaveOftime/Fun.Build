namespace Fun.Build

open System
open System.IO
open System.Text

type Changelog =

    static member GetLastVersion(directory, ?changelogFileName, ?isPreview, ?isValidVersion) =
        let changelogFileName = defaultArg changelogFileName "CHANGELOG.md"
        let lines = File.ReadLines(Path.Combine(directory, changelogFileName)).GetEnumerator()

        let isPreview = defaultArg isPreview (fun (x: string) -> x.Contains("preview"))
        let isValidVersion = defaultArg isValidVersion (fun (_: string) -> true)


        let mutable isDone = false
        let mutable version = None
        let mutable preview = true
        let releaseNotes = StringBuilder()

        while not isDone && lines.MoveNext() do
            let line = lines.Current.Trim()
            // We'd better to not release unreleased version
            if "## [Unreleased]".Equals(line, StringComparison.OrdinalIgnoreCase) then
                ()
            // Simple way to find the version string
            else if version.IsNone && line.StartsWith "## [" && line.Contains "]" then
                version <- Some(line.Substring(4, line.IndexOf("]") - 4))
                preview <- isPreview line
                // In the future we can verify version format according to more rules
                if isValidVersion line |> not then failwith "First number should be digit"
            else if version.IsSome then
                if line.StartsWith "## [" then
                    isDone <- true
                else
                    releaseNotes.AppendLine line |> ignore

        match version with
        | None -> None
        | Some v ->
            Some
                {|
                    Version = v
                    Preview = preview
                    ReleaseNotes = releaseNotes.ToString()
                |}
