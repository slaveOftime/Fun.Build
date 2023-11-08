module Fun.Build.Tests.StageContextExtensions

open Xunit
open Fun.Build
open Fun.Build.StageContextExtensionsInternal


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
let ``GetAllEnvVars should work`` () =
    let pipeline =
        pipeline "" {
            envVars [ "test1", "e1"; "test2", "e1"; "test3", "e3" ]
            stage "" { envVars [ "test2", "e2" ] }
        }

    let result = pipeline.Stages[0].GetAllEnvVars()
    Assert.Equal("e1", Map.find "test1" result)
    Assert.Equal("e2", Map.find "test2" result)
    Assert.Equal("e3", Map.find "test3" result)


[<Fact>]
let ``GetAllCmdArgs should work`` () =
    let pipeline =
        pipeline "" {
            cmdArgs [ "test3"; "c1" ]
            stage "" { echo "" }
        }

    Assert.Equal<string list>([ "test3"; "c1" ], pipeline.Stages[0].GetAllCmdArgs())


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
            noPrefixForStep false
            stage "" { noPrefixForStep false }
            stage "" { run ignore }
        }

    Assert.Equal(false, pipeline1.Stages[0].GetNoPrefixForStep())
    Assert.Equal(false, pipeline1.Stages[1].GetNoPrefixForStep())

    let pipeline2 =
        pipeline "" {
            stage "" { noPrefixForStep false }
            stage "" { run ignore }
        }

    Assert.Equal(false, pipeline2.Stages[0].GetNoPrefixForStep())
    Assert.Equal(true, pipeline2.Stages[1].GetNoPrefixForStep())


[<Fact>]
let ``noStdRedirectForStep should work`` () =
    let pipeline1 =
        pipeline "" {
            noStdRedirectForStep
            stage "" { noStdRedirectForStep }
            stage "" { run ignore }
        }

    Assert.Equal(true, pipeline1.Stages[0].GetNoStdRedirectForStep())
    Assert.Equal(true, pipeline1.Stages[1].GetNoStdRedirectForStep())

    let pipeline2 =
        pipeline "" {
            stage "" { noStdRedirectForStep }
            stage "" { run ignore }
        }

    Assert.Equal(true, pipeline2.Stages[0].GetNoStdRedirectForStep())
    Assert.Equal(false, pipeline2.Stages[1].GetNoStdRedirectForStep())


[<Fact>]
let ``RunCommandCaptureOutput should work`` () =
    pipeline "" {
        stage "" {
            whenAny {
                platformOSX
                platformLinux
            }
            run (fun ctx -> async {
                let! result = ctx.RunCommandCaptureOutput "echo 42"
                Assert.Equal(Ok "42\n\n", result)
            })
        }
        stage "" {
            whenWindows
            run (fun ctx -> async {
                let! result = ctx.RunCommandCaptureOutput "powershell echo 42"
                Assert.Equal(Ok "42\r\n\r\n", result)
            })
        }
        runImmediate
    }

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
            timeout 2
            stage "" {
                paralle
                run (fun _ -> async {
                    while true do
                        do! Async.Sleep 100
                })
                run (fun ctx -> async {
                    while true do
                        do! Async.Sleep 100
                        j <- j + 1
                        printfn $"task2 {i}"
                        if i > 3 then ctx.SoftCancelStep()
                })
                run (fun ctx -> async {
                    while true do
                        do! Async.Sleep 100
                        i <- i + 1
                        printfn $"task1 {i}"
                        if i > 5 then ctx.SoftCancelStage()
                })
            }
            stage "" { run call }
            runImmediate
        }
    )

    Assert.True(10 <= i + j)


[<Fact>]
let ``FailIfNoActiveSubStage`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            stage "" {
                failIfNoActiveSubStage
                stage "" { when' false }
            }
            runImmediate
        }
    )
    |> ignore

    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            stage "" {
                failIfNoActiveSubStage
                echo ""
            }
            runImmediate
        }
    )
    |> ignore

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                failIfNoActiveSubStage
                stage "" { when' false }
                stage "" {
                    when' true
                    run (ignore >> call)
                }
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                failIfNoActiveSubStage
                echo ""
                stage "" {
                    when' true
                    run (ignore >> call)
                }
            }
            runImmediate
        }
    )
