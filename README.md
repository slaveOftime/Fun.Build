# Fun.Build

This is a project mainly used for CICD, you can use it in a fsharp project or as a script. You can check the **build.fsx** under the root folder to check how the Fun.Build project itself is built and published to nuget.

The basic idea is you have **pipeline** which can contain multiple stages.  
Every **stage** can contain multiple steps. In the stage you can set it to run in parallel or run under some conditions (when envVar, cmdArg, branch etc.).  
Every **step** is just a **async< int >**, int is for the exit code. 


## For what

- Simple and straight forward DSL
- Type safety and extendable DSL
- Build and compose complex pipelines
- Test your pipelines locally


## Example:

```fsharp
#r "nuget: Fun.Build, 0.0.7"

open Fun.Build

pipeline "Fun.Build" {
    timeout 30 // You can set global default timeout for every step
    stage "Check environment" {
        timeout 30 // You can set default timeout for every step under the stage
        run "dotnet --version" // You can run command directly with a string
        run "dotnet --list-sdks"
        run (fun ctx -> printfn $"""GITHUB_ACTION: {ctx.GetEnvVar "GITHUB_ACTION"}""")
    }
    stage "Run unit tests" { run "dotnet test" }
    stage "Build packages" { run "dotnet pack -c Release Fun.Build/Fun.Build.fsproj -o ." }
    stage "Publish packages to nuget" {
        // whenAny, whenNot, whenAll. They can also be composed.
        whenAll { // Means only all conditions are matched then the current stage can be active
            branch "master" // Check current branch is master
            envVar "NUGET_API_KEY" // Check has env variable
        }
        add (fun ctx ->
            // use cmd so we can make sure sensitive information in FormatableString is encoded
            cmd $"""dotnet nuget push *.nupkg -s https://api.nuget.org/v3/index.json -k {ctx.GetEnvVar "NUGET_API_KEY"} --skip-duplicate"""
        )
    }
    post [ // Post stages are optional. It will run even other normal stages are failed.
        stage "Post stage" {
            run (fun _ -> async {
                0 // do something
            })
        }
    ]
    // You can have multiple pipelines, sometimes you only want to run it only if the command specified the pipeline name.
    // If this is set to false, then it will always run if you do not specify which pipeline to run.
    // To specify you can do this: dotnet fsi build.fsx -p Fun.Build
    runIfOnlySpecified false
}
```
