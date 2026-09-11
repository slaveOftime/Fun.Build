module Fun.Build.Tests.PipelineBuilderTests

open Xunit
open Fun.Build
open System.Diagnostics
open System.Threading.Tasks


[<Fact>]
let ``pipeline should world with multiple stages with different conditions`` () =
    shouldNotBeCalled (fun call -> pipeline "" {
        stage "" {
            whenCmdArg "test1"
            run call
        }
        stage "" {
            whenEnvVar "test2"
            run call
        }
        runImmediate
    })

    shouldBeCalled (fun call -> pipeline "" {
        cmdArgs [ "test1" ]
        stage "" {
            whenCmdArg "test1"
            run call
        }
        runImmediate
    })

    shouldBeCalled (fun call -> pipeline "" {
        envVars [ "test2", "" ]
        stage "" {
            whenEnvVar "test2"
            run call
        }
        runImmediate
    })

    shouldNotBeCalled (fun call -> pipeline "" {
        stage "" {
            whenAll {
                cmdArg "test1"
                envVar "test2"
            }
            run call
        }
        runImmediate
    })

    shouldNotBeCalled (fun call -> pipeline "" {
        cmdArgs [ "test1" ]
        stage "" {
            whenAll {
                cmdArg "test1"
                envVar "test2"
            }
            run call
        }
        runImmediate
    })

    shouldBeCalled (fun call -> pipeline "" {
        cmdArgs [ "test1" ]
        envVars [ "test2", "" ]
        stage "" {
            timeout 1000
            whenAll {
                cmdArg "test1"
                envVar "test2"
            }
            timeout 1000
            run call
        }
        runImmediate
    })

    shouldBeCalled (fun call -> pipeline "" {
        stage "" { run call }
        runImmediate
    })

    shouldBeCalled (fun call -> pipeline "" {
        stage "" { run ignore }
        stage "" { run call }
        stage "" {
            paralle
            run "dotnet --version"
            run "dotnet --list-sdks"
        }
        runImmediate
    })


[<Fact>]
let ``post stage should always run when other stage is failed`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldBeCalled (fun call -> pipeline "" {
            stage "" {
                run (fun _ ->
                    failwith "test"
                    ()
                )
            }
            post [ stage "" { run call } ]
            runImmediate
        })
    )
    |> ignore

    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldBeCalled (fun call -> pipeline "" {
            stage "" { run (fun _ -> -1) }
            post [ stage "" { run call } ]
            runImmediate
        })
    )
    |> ignore



[<Fact>]
let ``all post stages should always run when some post stages are failed`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldBeCalled (fun call -> pipeline "" {
            post [
                stage "" {
                    run (fun _ ->
                        failwith "test"
                        ()
                    )
                }
                stage "" { run call }
            ]
            runImmediate
        })
    )
    |> ignore

    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldBeCalled (fun call -> pipeline "" {
            post [ stage "" { run (fun _ -> -1) }; stage "" { run call } ]
            runImmediate
        })
    )
    |> ignore


[<Fact>]
let ``runIfOnlySpecified should work`` () =
    shouldBeCalled (fun call -> pipeline "demo" {
        cmdArgs [ "-p"; "demo" ]
        stage "" { run call }
        runIfOnlySpecified
    })

    shouldNotBeCalled (fun call -> pipeline "demo" {
        stage "" { run call }
        runIfOnlySpecified
    })

    shouldBeCalled (fun call -> pipeline "demo" {
        timeout 1
        stage "" { run call }
        runIfOnlySpecified false
    })

[<Fact>]
let ``runIfOnlySpecified should work for multiple -p`` () =
    shouldBeCalled (fun call ->
        let p1 = "demo1"
        let p2 = "demo2"
        let args = [ "-p"; p1; "d1"; "--"; "d1-1"; "-p"; p2; "d2"; "--"; "d2-2" ]
        pipeline p1 {
            cmdArgs args
            stage "" {
                run (fun ctx ->
                    call ctx
                    Assert.Equal([| "-p"; p1; "d1" |], ctx.GetAllCmdArgs())
                    Assert.Equal([| "d1-1" |], ctx.GetRemainingCmdArgs())
                )
            }
            runIfOnlySpecified
        }
        pipeline p2 {
            cmdArgs args
            stage "" {
                run (fun ctx ->
                    call ctx
                    Assert.Equal([| "-p"; p2; "d2" |], ctx.GetAllCmdArgs())
                    Assert.Equal([| "d2-2" |], ctx.GetRemainingCmdArgs())
                )
            }
            runIfOnlySpecified
        }
    )


[<Fact>]
let ``parallel should work`` () =
    let sw = Stopwatch.StartNew()
    pipeline "" {
        stage "" {
            paralle
            run (Async.Sleep 1000)
            run (Async.Sleep 1000)
            run (fun _ -> Task.Delay 1000)
        }
        runImmediate
    }
    // Upper bounds are deliberately loose: these are wall-clock assertions and CI runners
    // are shared and slow. What each bound encodes:
    //   parallel   - must stay well under the 3000ms the sequential version needs
    //   sequential - must be at least 2500ms, which is what proves it did not run in parallel
    Assert.InRange(sw.ElapsedMilliseconds, 1000, 2400)

    sw.Restart()
    pipeline "" {
        stage "" {
            run (Async.Sleep 1000)
            run (Async.Sleep 1000)
            run (fun _ -> Task.Delay 1000)
        }
        runImmediate
    }
    let elapsed = sw.ElapsedMilliseconds
    Assert.InRange(elapsed, 2500, 10000)


[<Fact>]
let ``Syntax check`` () =
    let stage1 = stage "" { run ignore }

    pipeline "" { stage "" { whenCmdArg "" } } |> ignore

    pipeline "" { stage1 } |> ignore

    pipeline "" {
        let a = ""
        let b = {| Test = "value" |}
        runImmediate
    }

    pipeline "" {
        let a = ""
        let b = {| Test = "value" |}
        runIfOnlySpecified
    }

    pipeline "" {
        stage "" {
            let a = ""
            let b = {| Test = "value" |}
            echo ""
        }
    }
    |> ignore

    pipeline "" {
        cmdArgs [ "" ]
        envVars [ "", "" ]
        stage1
        stage "" { run ignore }
        stage "" {
            timeout 123
            whenAll {
                whenAny {
                    cmdArg ""
                    cmdArg "" ""
                }
            }
        }
    }
    |> ignore

    pipeline "" {
        stage "" {
            run ""
            run "" ""
            run (fun _ -> "")
            run (fun _ -> ())
            run (fun _ -> 0)
            run (fun _ -> async { () })
            run (fun _ -> async { return 0 })
            run (fun _ -> task { () })
            run (fun _ -> task { return 0 })
            run (fun _ -> Task.Delay 10)
            run (Async.Sleep 10)
            runSensitive $""
            run (Async.Sleep 10)
            step (fun ctx _ -> async { return Ok() })
        }
    }
    |> ignore


[<Fact>]
let ``Verification should work`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun call -> pipeline "" {
            whenCmdArg "123"
            stage "" { run call }
            post [ stage "" { run call } ]
            runImmediate
        })
    )
    |> ignore

    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun call -> pipeline "" {
            verify (fun _ -> false)
            stage "" { run call }
            post [ stage "" { run call } ]
            runImmediate
        })
    )
    |> ignore

    shouldBeCalled (fun call -> pipeline "" {
        cmdArgs [ "123" ]
        whenAll { cmdArg "123" }
        stage "" { run call }
        post [ stage "" { run call } ]
        runImmediate
    })

[<Fact>]
let ``when' stage should use stage execution result as verification condition for pipeline`` () =
    let mutable numChecks = 0
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun call -> pipeline "" {
            when' (
                stage "whenStageShouldFail" {
                    run (fun ctx ->
                        numChecks <- numChecks + 1
                        1
                    )
                }
            )
            stage "" { run call }
            runImmediate
        })
    )
    |> ignore
    Assert.Equal(1, numChecks)

    shouldBeCalled (fun call -> pipeline "" {
        when' (stage "whenStageShouldPass" { run (fun ctx -> 0) })
        stage "" { run call }
        runImmediate
    })



[<Fact>]
let ``Should fail if stage is ignored`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun call -> pipeline "" {
            stage "" {
                whenCmdArg "123"
                failIfIgnored
                run call
            }
            runImmediate
        })
    )
    |> ignore

    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun call -> pipeline "" {
            stage "" {
                stage "" {
                    whenCmdArg "123"
                    failIfIgnored
                    run call
                }
            }
            runImmediate
        })
    )
    |> ignore

    shouldBeCalled (fun call -> pipeline "1" {
        cmdArgs [ "123" ]
        stage "2" {
            stage "3" {
                whenCmdArg "123"
                failIfIgnored
                run call
            }
        }
        runImmediate
    })


[<Fact>]
let ``whenCmdArg should work`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun call -> pipeline "" {
            whenCmdArg "test1"
            stage "" { run call }
            runImmediate
        })
    )
    |> ignore

    shouldBeCalled (fun call -> pipeline "" {
        cmdArgs [ "test1" ]
        whenCmdArg "test1"
        stage "" { run call }
        runImmediate
    })

    shouldBeCalled (fun call -> pipeline "" {
        whenCmdArg "test1" "value" "description" true
        stage "" { run call }
        runImmediate
    })

    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun call -> pipeline "" {
            whenCmdArg "test1" "value" "description" false
            stage "" { run call }
            runImmediate
        })
    )
    |> ignore

    shouldBeCalled (fun call -> pipeline "" {
        cmdArgs [ "test1"; "value" ]
        whenCmdArg "test1" "value" "description" false
        stage "" { run call }
        runImmediate
    })



[<Fact>]
let ``runBeforeEachStage and runAfterEachStage should work`` () =
    let mutable i = 0
    let mutable j = 0
    let mutable ti = 0
    let mutable tj = 0

    pipeline "" {
        runBeforeEachStage (fun ctx ->
            i <- i + 1
            if ctx.GetStageLevel() = 0 then ti <- ti + 1
        )
        runAfterEachStage (fun ctx ->
            j <- j + 1
            if ctx.GetStageLevel() = 0 then tj <- tj + 1
        )
        stage "" { run ignore }
        stage "" { stage "" { run ignore } }
        post [ stage "" { run ignore } ]
        runImmediate
    }

    Assert.Equal(4, i)
    Assert.Equal(4, j)
    Assert.Equal(3, ti)
    Assert.Equal(3, tj)

[<Fact>]
let ``check GetAllCmdArgs and RemainingArgs works when remaining args is not empty`` () =
    let mutable actualAllCmdArgs = []
    let mutable actualRemainingArgs = []

    pipeline "demo" {
        cmdArgs [ "-p"; "demo"; "test1"; "v1"; "--"; "test2"; "v2" ]
        stage "shows arguments" {
            run (fun ctx ->
                actualAllCmdArgs <- ctx.GetAllCmdArgs()
                actualRemainingArgs <- ctx.GetRemainingCmdArgs()
            )
        }
        runImmediate
    }

    Assert.Equal<string list>([ "-p"; "demo"; "test1"; "v1" ], actualAllCmdArgs)
    Assert.Equal<string list>([ "test2"; "v2" ], actualRemainingArgs)

[<Fact>]
let ``check GetAllCmdArgs and RemainingArgs works when remaining args is provided but empty`` () =
    let mutable actualAllCmdArgs = []
    let mutable actualRemainingArgs = []

    pipeline "demo" {
        cmdArgs [ "-p"; "demo"; "test1"; "v1"; "--" ]
        stage "shows arguments" {
            run (fun ctx ->
                actualAllCmdArgs <- ctx.GetAllCmdArgs()
                actualRemainingArgs <- ctx.GetRemainingCmdArgs()
            )
        }
        runImmediate
    }

    Assert.Equal<string list>([ "-p"; "demo"; "test1"; "v1" ], actualAllCmdArgs)
    Assert.Equal<string list>([], actualRemainingArgs)


[<Fact>]
let ``check GetAllCmdArgs and RemainingArgs works when remaining args is not provided`` () =
    let mutable actualAllCmdArgs = []
    let mutable actualRemainingArgs = []

    pipeline "demo" {
        cmdArgs [ "-p"; "demo"; "test1"; "v1" ]
        stage "shows arguments" {
            run (fun ctx ->
                actualAllCmdArgs <- ctx.GetAllCmdArgs()
                actualRemainingArgs <- ctx.GetRemainingCmdArgs()
            )
        }
        runImmediate
    }

    Assert.Equal<string list>([ "-p"; "demo"; "test1"; "v1" ], actualAllCmdArgs)
    Assert.Equal<string list>([], actualRemainingArgs)

[<Fact>]
let ``help without -p should not run default pipeline`` () =
    let mutable called = false

    pipeline "Build" {
        cmdArgs [ "--help" ]
        stage "build" { run (fun _ -> called <- true) }
        runIfOnlySpecified false
    }

    Assert.False(called)

[<Fact>]
let ``help with -p should not run pipeline`` () =
    let mutable called = false

    pipeline "Build" {
        cmdArgs [ "-p"; "Build"; "--help" ]
        stage "build" { run (fun _ -> called <- true) }
        runIfOnlySpecified
    }

    Assert.False(called)

[<Fact>]
let ``help without -p should not run any pipeline when multiple exist`` () =
    let mutable buildCalled = false
    let mutable otherCalled = false

    pipeline "Build" {
        cmdArgs [ "--help" ]
        stage "build" { run (fun _ -> buildCalled <- true) }
        runIfOnlySpecified false
    }
    pipeline "Other" {
        cmdArgs [ "--help" ]
        stage "other" { run (fun _ -> otherCalled <- true) }
        runIfOnlySpecified true
    }

    Assert.False(buildCalled)
    Assert.False(otherCalled)
