#r "nuget: Fun.Build"

open System.IO
open Fun.Result
open Fun.Build
open Fun.Build.Github


let (</>) x y = Path.Combine(x, y)


let options = {|
    NugetAPIKey = EnvArg.Create("NUGET_API_KEY", description = "Nuget api key")
|}


let stage_checkEnv =
    stage "Check environment" {
        run (fun _ -> Spectre.Console.AnsiConsole.MarkupLine($"""[red]::error title=STAGE asd::check/step-0> One or more errors occurred. (Errors: Package 
Microsoft.AspNetCore.Components.QuickGrid should be updated from 8.0.8 to 8.0.10
Package Microsoft.AspNetCore.Components.Web should be updated from 8.0.8 to 
8.0.10
Package Microsoft.AspNetCore.Components.Authorization should be updated from 
8.0.8 to 8.0.10
Package Microsoft.FluentUI.AspNetCore.Components should be updated from 4.10.1 
to 4.10.2
Package MudBlazor should be updated from 7.8.0 to 7.13.0
Package Blazor-ApexCharts should be updated from 3.4.0 to 3.5.0) Errors: Package
Microsoft.AspNetCore.Components.QuickGrid should be updated from 8.0.8 to 8.0.10
Package Microsoft.AspNetCore.Components.Web should be updated from 8.0.8 to 
8.0.10
Package Microsoft.AspNetCore.Components.Authorization should be updated from 
8.0.8 to 8.0.10
Package Microsoft.FluentUI.AspNetCore.Components should be updated from 4.10.1 
to 4.10.2
Package MudBlazor should be updated from 7.8.0 to 7.13.0
Package Blazor-ApexCharts should be updated from 3.4.0 to 3.5.0
    [/]"""))
        run "dotnet tool restore"
    }

let stage_lint =
    stage "Lint" {
        stage "Format" { run "dotnet fantomas . -r" }
        stage "Check" {
            whenGithubAction
            run "dotnet fantomas . -r --check"
        }
    }

let stage_test = stage "Run unit tests" { run "dotnet test -v m" }

let stage_buildVersion =
    stage "generate Directory.build.props for version control" {
        run (fun _ ->
            Directory.GetDirectories(__SOURCE_DIRECTORY__)
            |> Seq.filter (fun x -> File.Exists(x </> "CHANGELOG.md"))
            |> Seq.iter (fun dir ->
                let version = Changelog.GetLastVersion dir |> Option.defaultWith (fun _ -> failwith "No version available")
                let content =
                    $"""<!-- auto generated -->
<Project>
    <PropertyGroup>
        <Version>{version.Version}</Version>
    </PropertyGroup>
</Project>"""
                File.WriteAllText(dir </> "Directory.Build.props", content)
            )
        )
    }


pipeline "packages" {
    description "Build and deploy to nuget"
    collapseGithubActionLogs
    stage_checkEnv
    stage_buildVersion
    stage_lint
    stage_test
    stage "Build packages" {
        run "dotnet pack -c Release Fun.Build/Fun.Build.fsproj -o ."
        run "dotnet pack -c Release Fun.Build.Cli/Fun.Build.Cli.fsproj -o ."
    }
    stage "Publish packages to nuget" {
        whenBranch "master"
        whenEnvVar options.NugetAPIKey
        whenEnvVar "GITHUB_ENV" "" "Only push packages in github action"
        whenGithubAction
        run (fun ctx ->
            let key = ctx.GetEnvVar options.NugetAPIKey.Name
            ctx.RunSensitiveCommand $"""dotnet nuget push *.nupkg -s https://api.nuget.org/v3/index.json --skip-duplicate -k {key}"""
        )
    }
    runIfOnlySpecified
}

pipeline "test" {
    description "Format code and run tests"
    collapseGithubActionLogs
    stage_checkEnv
    stage_buildVersion
    stage_lint
    stage_test
    runIfOnlySpecified
}


tryPrintPipelineCommandHelp ()
