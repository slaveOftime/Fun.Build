#r "nuget: Fun.Build, 0.0.7"
#r "nuget: Fake.IO.FileSystem, 5.20.4"

// for local dev only
//#r "nuget: Cliwrap"
//#r "nuget: Spectre.Console"
//#r "Fun.Build/bin/Debug/netstandard2.0/Fun.Build.dll"

open Fun.Build
open Fake.IO
open Fake.IO.Globbing.Operators


pipeline "Fun.Build" {
    timeout 30
    stage "Check environment" {
        run "dotnet --version"
        run "dotnet --list-sdks"
        run (fun ctx -> printfn $"""GITHUB_ACTION: {ctx.GetEnvVar "GITHUB_ACTION"}""")
    }
    stage "Run unit tests" { run "dotnet test" }
    stage "Build packages" { run "dotnet pack -c Release Fun.Build/Fun.Build.fsproj -o ." }
    stage "Publish packages to nuget" {
        whenAll {
            branch "master"
            envVar "NUGET_API_KEY"
        }
        add (fun ctx ->
            // use cmd so we can make sure sensitive information in FormatableString is encoded
            cmd $"""dotnet nuget push *.nupkg -s https://api.nuget.org/v3/index.json -k {ctx.GetEnvVar "NUGET_API_KEY"} --skip-duplicate"""
        )
    }
    post [
        stage "Post stage" {
            whenNot { envVar "GITHUB_ACTION" }
            run (fun _ -> File.deleteAll !! "*.nupkg")
        }
    ]
    runIfOnlySpecified false
}
