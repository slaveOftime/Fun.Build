//sdk Microsoft.NET.Sdk
//property TargetFramework=net10.0

#r "nuget: FsHttp, 15.0.3"
#r "nuget: Fun.Build, 1.1.17"

#load "demo-cmd.fsx"


printfn "__SOURCE_DIRECTORY__ %s" __SOURCE_DIRECTORY__

printfn "Env CommandArgs: %A" (System.Environment.GetCommandLineArgs())
printfn "Fsi CommandArgs: %A" (fsi.CommandLineArgs)

printfn "%A" (System.Text.Json.JsonSerializer.Serialize  fsi)

printfn "Iteration 1"
