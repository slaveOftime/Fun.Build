//property TargetFramework=net10.0

#r "nuget: Fun.Build, 1.1.17"

open Fun.Build

let script = "./fsx-demo.fsx"

pipeline "eveluate" {
    stage "setup" {
        run "dotnet build"
        stage "build fsx" {
            workingDir "./Fun.Fsx"
            whenNot { cmdArg "--no-setup" }
            run "dotnet publish -c Release -o dist"
        }
    }
    stage "fsx" { 
        whenWindows
        run $"./Fun.Fsx/dist/fsx.exe {script} -v diag -- -arg1 1"
    }
    stage "fsx" { 
        whenLinux
        run $"./Fun.Fsx/dist/fsx {script} -v diag -- -arg1 1"
    }
    stage "fsi" { run $"dotnet fsi {script} -- -arg1 1" }
    runIfOnlySpecified false
}
