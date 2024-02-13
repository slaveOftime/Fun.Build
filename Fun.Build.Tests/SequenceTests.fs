module Fun.Build.Tests.SequenceTests

open Xunit
open Fun.Build


[<Fact>]
let ``stages should run in sequence`` () =
    let calls = System.Collections.Generic.List<int>()

    pipeline "" {
        stage "test1" { run (fun _ -> calls.Add 1) }
        stage "test2" { run (fun _ -> calls.Add 2) }
        stage "test3" { run (fun _ -> calls.Add 3) }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3 ], calls)

    calls.Clear()
    pipeline "" {
        timeout 20
        stage "test1" { run (fun _ -> calls.Add 1) }
        stage "test2" { run (fun _ -> calls.Add 2) }
        timeout 20
        stage "test3" { run (fun _ -> calls.Add 3) }
        timeout 20
        runIfOnlySpecified false
    }
    Assert.Equal<int>([ 1; 2; 3 ], calls)


[<Fact>]
let ``steps should run in sequence`` () =
    let calls = System.Collections.Generic.List<int>()

    pipeline "" {
        stage "" {
            run (fun _ -> calls.Add 1)
            stage "" { run (fun _ -> calls.Add 2) }
            run (fun _ -> calls.Add 3)
            stage "" { run (fun _ -> calls.Add 4) }
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3; 4 ], calls)

    calls.Clear()
    pipeline "" {
        stage "" {
            timeout 10
            run (fun _ -> calls.Add 1)
            run (fun _ -> calls.Add 2)
            timeout 10
            run (fun _ -> calls.Add 3)
            timeout 10
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3 ], calls)

    calls.Clear()
    pipeline "" {
        stage "" {
            step (fun _ _ -> async {
                calls.Add 1
                return Ok()
            })
            step (fun _ _ -> async {
                calls.Add 2
                return Ok()
            })
            step (fun _ _ -> async {
                calls.Add 3
                return Ok()
            })
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3 ], calls)

    calls.Clear()
    pipeline "" {
        stage "" {
            timeout 10
            step (fun _ _ -> async {
                calls.Add 1
                return Ok()
            })
            run (fun _ ->
                calls.Add 2
                Ok()
            )
            step (fun _ _ -> async {
                calls.Add 3
                return Ok()
            })
            timeout 10
            step (fun _ _ -> async {
                calls.Add 4
                return Ok()
            })
            timeout 10
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3; 4 ], calls)



[<Fact>]
let ``nested stages should run in sequence`` () =
    let calls = System.Collections.Generic.List<int>()

    pipeline "" {
        stage "" { run (fun _ -> calls.Add 1) }
        stage "" {
            run (fun _ -> calls.Add 2)
            stage "" {
                stage "" { run (fun _ -> calls.Add 3) }
                run (fun _ -> calls.Add 4)
            }
        }
        stage "" {
            stage "" { run (fun _ -> calls.Add 5) }
            stage "" { run (fun _ -> calls.Add 6) }
            stage "" { run (fun _ -> calls.Add 7) }
        }
        stage "" {
            stage "" {
                whenCmdArg "test"
                run (fun _ -> calls.Add 8)
            }
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3; 4; 5; 6; 7 ], calls)

[<Fact>]
let ``custom exit code should pass for stage`` () =
    pipeline "" {
        stage "" {
            acceptExitCodes [| 99 |]
            run (fun _ -> async { return 99 })
        }
        runImmediate
    }

[<Fact>]
let ``custom exit code should pass for pipeline`` () =
    pipeline "" {
        acceptExitCodes [| 99 |]
        stage "" { run (fun _ -> async { return 99 }) }
        runImmediate
    }

[<Fact>]
let ``custom exit code should pass for nested stage`` () =
    pipeline "" {
        stage "" {
            acceptExitCodes [| 99 |]
            stage "nested" { run (fun _ -> async { return 99 }) }
            stage "nested2" {
                acceptExitCodes [ 123 ]
                run (fun _ -> 123)
            }
        }
        runImmediate
    }


[<Fact>]
let ``shuffleExecuteSequence should work`` () =
    let ls = System.Collections.Generic.List()

    pipeline "" {
        stage "" {
            shuffleExecuteSequence
            run (fun _ -> ls.Add 1)
            run (fun _ -> ls.Add 2)
            run (fun _ -> ls.Add 3)
            run (fun _ -> ls.Add 4)
            run (fun _ -> ls.Add 5)
        }
        runImmediate
    }

    Assert.False(List.ofSeq ls = [ 1; 2; 3; 4 ])


[<Fact>]
let ``for loop or yield! should work`` () =
    let list = System.Collections.Generic.List()

    pipeline "" {
        stage "" {
            failIfIgnored
            for i in 1..5 do
                stage $"{i}" { run (fun _ -> list.Add i) }
            yield! [ stage "" { run (fun _ -> list.Add 6) } ]
        }
        runImmediate
    }

    Assert.Equal<int>([ 1..6 ], list)



[<Fact>]
let ``continueOnStepFailure should work`` () =
    let list = System.Collections.Generic.List()
    pipeline "" {
        stage "" {
            continueOnStepFailure
            run (fun _ -> list.Add(1))
            run (fun _ ->
                list.Add(2)
                Error ""
            )
            run (fun _ -> list.Add(3))
        }
        stage "" { run (fun _ -> list.Add(4)) }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3; 4 ], list)

    shouldBeCalled (fun fn ->
        pipeline "" {
            stage "" {
                paralle
                continueOnStepFailure
                run (fun _ -> Ok())
                run (fun _ -> Error "")
                run (fun _ -> Ok())
            }
            stage "" { run fn }
            runImmediate
        }
    )

    list.Clear()
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun fn ->
            pipeline "" {
                stage "" {
                    continueOnStepFailure false
                    run (fun _ -> list.Add(1))
                    run (fun _ ->
                        list.Add(2)
                        Error ""
                    )
                    run (fun _ -> Ok())
                }
                stage "" { run fn }
                runImmediate
            }
        )
    )
    |> ignore
    Assert.Equal<int>([ 1; 2 ], list)

    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun fn ->
            pipeline "" {
                stage "" {
                    paralle
                    continueOnStepFailure false
                    run (fun _ -> Ok())
                    run (fun _ -> Error "")
                    run (fun _ -> Ok())
                }
                stage "" { run fn }
                runImmediate
            }
        )
    )
    |> ignore

    list.Clear()
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun fn ->
            pipeline "" {
                stage "" {
                    run (fun _ -> list.Add(1))
                    run (fun _ ->
                        list.Add(2)
                        Error ""
                    )
                    run (fun _ -> Ok())
                }
                stage "" { run fn }
                runImmediate
            }
        )
    )
    |> ignore
    Assert.Equal<int>([ 1; 2 ], list)

    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun fn ->
            pipeline "" {
                stage "" {
                    paralle
                    run (fun _ -> Ok())
                    run (fun _ -> Error "")
                    run (fun _ -> Ok())
                }
                stage "" { run fn }
                runImmediate
            }
        )
    )
    |> ignore

    list.Clear()
    pipeline "" {
        stage "" {
            continueOnStepFailure
            run (fun _ -> list.Add(1))
            stage "" {
                run (fun _ ->
                    list.Add(2)
                    Error ""
                )
                run (fun _ -> list.Add(5))
            }
            run (fun _ -> list.Add(3))
        }
        stage "" { run (fun _ -> list.Add(4)) }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3; 4 ], list)


[<Fact>]
let ``continueStepsOnFailure should work`` () =
    let list = System.Collections.Generic.List()
    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            stage "" {
                continueStepsOnFailure
                run (fun _ -> list.Add(1))
                run (fun _ ->
                    list.Add(2)
                    Error ""
                )
                run (fun _ -> list.Add(3))
            }
            stage "" { run (fun _ -> list.Add(4)) }
            runImmediate
        }
    )
    |> ignore
    Assert.Equal<int>([ 1; 2; 3 ], list)

    list.Clear()
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun fn ->
            pipeline "" {
                stage "" {
                    paralle
                    continueStepsOnFailure
                    run (fun _ -> list.Add(1))
                    run (fun _ ->
                        list.Add(2)
                        Error ""
                    )
                    run (fun _ -> list.Add(3))
                }
                stage "" { run fn }
                runImmediate
            }
        )
    )
    |> ignore
    Assert.Equal<int>([ 1; 2; 3 ], Seq.sort list)

    list.Clear()
    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            stage "" {
                continueStepsOnFailure false
                run (fun _ -> list.Add(1))
                run (fun _ ->
                    list.Add(2)
                    Error ""
                )
                run (fun _ -> list.Add(3))
            }
            stage "" { run (fun _ -> list.Add(4)) }
            runImmediate
        }
    )
    |> ignore
    Assert.Equal<int>([ 1; 2 ], list)

    list.Clear()
    Assert.Throws<PipelineFailedException>(fun _ ->
        shouldNotBeCalled (fun fn ->
            pipeline "" {
                stage "" {
                    paralle
                    continueStepsOnFailure false
                    run (fun _ -> list.Add(1))
                    run (fun _ -> async {
                        do! Async.Sleep 100
                        list.Add(2)
                        return Error ""
                    })
                    run (fun _ -> async {
                        do! Async.Sleep 500
                        list.Add(3)
                    })
                }
                stage "" { run fn }
                runImmediate
            }
        )
    )
    |> ignore
    Assert.Equal<int>([ 1; 2 ], list)


[<Fact>]
let ``continueStageOnFailure should work`` () =
    let list = System.Collections.Generic.List()
    pipeline "" {
        stage "" {
            continueStageOnFailure
            run (fun _ -> list.Add(1))
            run (fun _ ->
                list.Add(2)
                Error ""
            )
            run (fun _ -> list.Add(3))
        }
        stage "" { run (fun _ -> list.Add(4)) }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 4 ], list)

    list.Clear()
    pipeline "" {
        stage "" {
            paralle
            continueStageOnFailure
            run (fun _ -> list.Add(1))
            run (fun _ -> async {
                do! Async.Sleep 100
                list.Add(2)
                failwith "demo"
                return Error ""
            })
            run (fun _ -> async {
                do! Async.Sleep 200
                list.Add(3)
            })
        }
        stage "" { run (fun _ -> list.Add(4)) }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 4 ], Seq.sort list)

    list.Clear()
    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            stage "" {
                continueStepsOnFailure true
                continueStageOnFailure false
                run (fun _ -> list.Add(1))
                run (fun _ ->
                    list.Add(2)
                    Error ""
                )
                run (fun _ -> list.Add(3))
            }
            stage "" { run (fun _ -> list.Add(4)) }
            runImmediate
        }
    )
    |> ignore
    Assert.Equal<int>([ 1; 2; 3 ], list)

    list.Clear()
    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            stage "" {
                paralle
                continueStepsOnFailure true
                continueStageOnFailure false
                run (fun _ -> list.Add(1))
                run (fun _ ->
                    list.Add(2)
                    Error ""
                )
                run (fun _ -> async {
                    do! Async.Sleep 200
                    list.Add(3)
                })
            }
            stage "" { run (fun _ -> list.Add(4)) }
            runImmediate
        }
    )
    |> ignore
    Assert.Equal<int>([ 1; 2; 3 ], Seq.sort list)


[<Fact>]
let ``continueStepsOnFailure for nested stage should work`` () =
    let list = System.Collections.Generic.List()
    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            stage "" {
                continueStepsOnFailure
                run (fun _ -> list.Add(1))
                stage "" {
                    run (fun _ ->
                        list.Add(2)
                        Error ""
                    )
                }
                run (fun _ -> list.Add(3))
            }
            stage "" { run (fun _ -> list.Add(4)) }
            runImmediate
        }
    )
    |> ignore
    Assert.Equal<int>([ 1; 2; 3 ], list)

    list.Clear()
    pipeline "" {
        stage "" {
            continueStageOnFailure
            run (fun _ -> list.Add(1))
            stage "" {
                run (fun _ ->
                    list.Add(2)
                    failwith ""
                    ()
                )
            }
            run (fun _ -> list.Add(3))
        }
        stage "" { run (fun _ -> list.Add(4)) }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 4 ], list)
