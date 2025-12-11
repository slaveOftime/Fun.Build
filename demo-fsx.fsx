//sdk Microsoft.NET.Sdk
//property OutputType=Exe
//property TargetFramework=net10.0

#r "nuget:FsHttp,15.0.3"
#r "nuget: Fun.Build"
#load "demo-cmd.fsx"
#load "demo.fsx"

System.Console.ReadLine() |> printfn " => %s"

printfn "Hello from demo-fsx! %s" __SOURCE_DIRECTORY__

printfn "%A" (System.Environment.GetCommandLineArgs())
 