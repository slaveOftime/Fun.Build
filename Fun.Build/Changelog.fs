namespace Fun.Build

open System
open System.IO
open System.Text

type Changelog =

    /// <summary>
    /// Get latest version info from the CHANGELOG.md file
    /// </summary>
    /// <param name="directory">Directory which contains the changelog file</param>
    /// <param name="changelogFileName">Default is CHANGELOG.md</param>
    /// <param name="isPreview">Check if the line contains keyword 'preview'</param>
    /// <param name="isValidVersion">By default always return true</param>
    static member GetLastVersion(directory, ?changelogFileName, ?isPreview, ?isValidVersion) =
        let changelogFileName = defaultArg changelogFileName "CHANGELOG.md"
        let lines = File.ReadLines(Path.Combine(directory, changelogFileName)).GetEnumerator()

        let isPreview = defaultArg isPreview (fun (x: string) -> x.Contains("preview"))
        let isValidVersion = defaultArg isValidVersion (fun (_: string) -> true)


        let mutable isDone = false
        let mutable version = None
        let mutable preview = true
        let mutable dateTime = None
        let releaseNotes = StringBuilder()

        while not isDone && lines.MoveNext() do
            let line = lines.Current.Trim()
            // We'd better to not release unreleased version
            if "## [Unreleased]".Equals(line, StringComparison.OrdinalIgnoreCase) then
                ()
            // Simple way to find the version string
            else if version.IsNone && line.StartsWith "## [" && line.Contains "]" then
                if isValidVersion line |> not then failwith "First number should be digit"

                version <- Some(line.Substring(4, line.IndexOf("]") - 4))
                preview <- isPreview line
                dateTime <-
                    let index = line.LastIndexOf("- ")
                    if index > -1 then line.Substring(index + 2) |> DateTime.Parse |> Some else None

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
                    DateTime = dateTime
                |}
