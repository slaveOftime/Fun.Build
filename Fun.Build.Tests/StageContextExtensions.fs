module Fun.Build.Tests.StageContextExtensions

open Xunit
open Fun.Build


[<Fact>]
let ``TryGetCmdArgOrEnvVar should work`` () =
    let pipeline =
        pipeline "" {
            envVars [ "test1", "e1"; "test2", "e1"; "test3", "e1" ]
            cmdArgs [ "test3"; "c1" ]
            stage "" { envVars [ "test2", "e2" ] }
        }

    Assert.Equal(ValueNone, pipeline.Stages[0].TryGetCmdArgOrEnvVar "abc")
    Assert.Equal(ValueSome "e1", pipeline.Stages[0].TryGetCmdArgOrEnvVar "test1")
    Assert.Equal(ValueSome "e2", pipeline.Stages[0].TryGetCmdArgOrEnvVar "test2")
    Assert.Equal(ValueSome "c1", pipeline.Stages[0].TryGetCmdArgOrEnvVar "test3")


[<Fact>]
let ``workingDir should work`` () =
    let pipeline =
        pipeline "" {
            workingDir "test1"
            stage "" { workingDir "test2" }
            stage "" { run ignore }
        }

    Assert.Equal(ValueSome "test1", pipeline.Stages[1].GetWorkingDir())
    Assert.Equal(ValueSome "test2", pipeline.Stages[0].GetWorkingDir())


[<Fact>]
let ``noPrefixForStep should work`` () =
    let pipeline1 =
        pipeline "" {
            noPrefixForStep
            stage "" {
                noPrefixForStep
                workingDir "test2"
            }
            stage "" { run ignore }
        }

    Assert.Equal(true, pipeline1.Stages[0].GetNoPrefixForStep())
    Assert.Equal(true, pipeline1.Stages[1].GetNoPrefixForStep())

    let pipeline2 =
        pipeline "" {
            stage "" {
                noPrefixForStep
                workingDir "test2"
            }
            stage "" { run ignore }
        }

    Assert.Equal(true, pipeline2.Stages[0].GetNoPrefixForStep())
    Assert.Equal(false, pipeline2.Stages[1].GetNoPrefixForStep())

[<Fact>]
let ``RunCommandCaptureOutput should work`` () =
    let mutable output = ""

    pipeline "" {
        stage "" {
            whenAny {
                platformOSX
                platformLinux
            }
            run (fun ctx -> async {
                let! result = ctx.RunCommandCaptureOutput "echo 42"
                match result with
                | Ok x -> output <- x
                | Error _ -> ()
            })
        }
        stage "" {
            whenWindows
            run (fun ctx -> async {
                let! result = ctx.RunCommandCaptureOutput "powershell echo 42"
                match result with
                | Ok x -> output <- x
                | Error _ -> ()
            })
        }
        runImmediate
    }

    Assert.Equal("42", output)

[<Fact>]
let ``RunCommandCaptureOutput should return an error if command failed`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldBeCalled (fun call ->
            pipeline "" {
                stage "" {
                    run (fun ctx -> async {
                        let! result = ctx.RunCommandCaptureOutput "thisCmdDoesNotExist"
                        return ()
                    })
                }
                runImmediate
            }
        )
    )
    |> ignore


[<Fact>]
let ``Soft cancel should work`` () =
    let mutable i = 0
    let mutable j = 0

    shouldBeCalled (fun call ->
        pipeline "" {
            timeout 10
            stage "" {
                paralle
                run (fun _ -> async {
                    while true do
                        do! Async.Sleep 1000
                })
                run (fun ctx -> async {
                    while true do
                        do! Async.Sleep 1000
                        j <- j + 1
                        printfn $"task2 {i}"
                        if i > 3 then ctx.SoftCancelStep()
                })
                run (fun ctx -> async {
                    while true do
                        do! Async.Sleep 1000
                        i <- i + 1
                        printfn $"task1 {i}"
                        if i > 5 then ctx.SoftCancelStage()
                })
            }
            stage "" { run call }
            runImmediate
        }
    )

    Assert.Equal(10, i + j)
