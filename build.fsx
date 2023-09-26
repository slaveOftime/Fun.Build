#r "nuget: Fun.Build, 0.5.2"
#r "nuget: Fake.IO.FileSystem, 5.23.0"

open System
open System.IO
open Fake.IO
open Fake.IO.Globbing.Operators
open Fun.Build


let options = {|
    GithubAction = EnvArg.Create("GITHUB_ACTION", description = "Run only in in github action container")
    NugetAPIKey = EnvArg.Create("NUGET_API_KEY", description = "Nuget api key")
|}

let getVersionFromChangelogFor dir =
    File.ReadLines(Path.Combine(dir, "CHANGELOG.md"))
    |> Seq.tryPick (fun line ->
        let line = line.Trim()
        // We'd better to not release unreleased version
        if "## [Unreleased]".Equals(line, StringComparison.OrdinalIgnoreCase) then
            None
        // Simple way to find the version string
        else if line.StartsWith "## [" && line.Contains "]" then
            let version = line.Substring(4, line.IndexOf("]") - 4)
            // In the future we can verify version format according to more rules
            if Char.IsDigit version[0] |> not then failwith "First number should be digit"
            Some(version)
        else
            None
    )

let stage_checkEnv =
    stage "Check environment" {
        run "dotnet tool restore"
        run (fun ctx -> printfn $"""github action name: {ctx.GetEnvVar options.GithubAction.Name}""")
    }

let stage_lint =
    stage "Lint" {
        stage "Format" {
            whenNot { envVar options.GithubAction }
            run "dotnet fantomas . -r"
        }
        stage "Check" {
            whenEnvVar options.GithubAction
            run "dotnet fantomas . -r --check"
        }
    }

let stage_test = stage "Run unit tests" { run "dotnet test" }


pipeline "Fun.Build" {
    description "Build and deploy to nuget"
    stage_checkEnv
    stage_lint
    stage_test
    stage "Build packages" {
        run (fun _ ->
            let version =
                getVersionFromChangelogFor __SOURCE_DIRECTORY__ |> Option.defaultWith (fun () -> failwith "Version is not found")
            $"dotnet pack -c Release Fun.Build/Fun.Build.fsproj -p:PackageVersion={version} -o ."
        )
    }
    stage "Publish packages to nuget" {
        whenBranch "master"
        whenEnvVar options.NugetAPIKey
        run (fun ctx ->
            let key = ctx.GetEnvVar options.NugetAPIKey.Name
            ctx.RunSensitiveCommand $"""dotnet nuget push *.nupkg -s https://api.nuget.org/v3/index.json --skip-duplicate -k {key}"""
        )
    }
    post [
        stage "Clean packages" {
            whenNot { envVar options.GithubAction }
            run (fun _ -> File.deleteAll !! "*.nupkg")
        }
    ]
    runIfOnlySpecified false
}

pipeline "test" {
    description "Format code and run tests"
    stage_checkEnv
    stage_lint
    stage_test
    runIfOnlySpecified
}


tryPrintPipelineCommandHelp ()
