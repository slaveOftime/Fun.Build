module Fun.Build.Tests.PipelineBuilderTests

open Xunit
open Fun.Build
open System.Diagnostics
open System.Threading.Tasks


[<Fact>]
let ``pipeline should world with mutiple stages with different conditions`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenCmdArg "test1"
                run call
            }
            stage "" {
                whenEnvVar "test2"
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "test1" ]
            stage "" {
                whenCmdArg "test1"
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            envArgs [ "test2", "" ]
            stage "" {
                whenEnvVar "test2"
                run call
            }
            runImmediate
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenAll {
                    cmdArg "test1"
                    envVar "test2"
                }
                run call
            }
            runImmediate
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "test1" ]
            stage "" {
                whenAll {
                    cmdArg "test1"
                    envVar "test2"
                }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "test1" ]
            envArgs [ "test2", "" ]
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
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" { run call }
            runImmediate
        }
    )
    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" { run ignore }
            stage "" { run call }
            runImmediate
        }
    )


[<Fact>]
let ``post stage should always run when other stage is failed`` () =
    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                run (fun _ ->
                    failwith "test"
                    ()
                )
            }
            post [ stage "" { run call } ]
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" { run (fun _ -> -1) }
            post [ stage "" { run call } ]
            runImmediate
        }
    )



[<Fact>]
let ``all post stages should always run when some post stages are failed`` () =
    shouldBeCalled (fun call ->
        pipeline "" {
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
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            post [ stage "" { run (fun _ -> -1) }; stage "" { run call } ]
            runImmediate
        }
    )


[<Fact>]
let ``timeout should work`` () =
    shouldBeCalled (fun call ->
        pipeline "" {
            timeout 1
            stage "" {
                run (
                    async {
                        do! Async.Sleep 100
                        call ()
                    }
                )
            }
            runImmediate
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "" {
            timeout 1
            stage "" {
                run (
                    async {
                        do! Async.Sleep 2000
                        call ()
                    }
                )
            }
            runImmediate
        }
    )


[<Fact>]
let ``runIfOnlySpecified should work`` () =
    shouldBeCalled (fun call ->
        pipeline "demo" {
            cmdArgs [ "-p"; "demo" ]
            stage "" { run call }
            runIfOnlySpecified
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "demo" {
            stage "" { run call }
            runIfOnlySpecified
        }
    )

    shouldBeCalled (fun call ->
        pipeline "demo" {
            timeout 1
            stage "" { run call }
            runIfOnlySpecified false
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
    Assert.True(sw.ElapsedMilliseconds < 3000)

    sw.Restart()
    pipeline "" {
        stage "" {
            run (Async.Sleep 1000)
            run (Async.Sleep 1000)
            run (fun _ -> Task.Delay 1000)
        }
        runImmediate
    }
    Assert.True(sw.ElapsedMilliseconds >= 3000)


[<Fact>]
let ``Syntax check`` () =
    let stage1 = stage "" { run ignore }

    pipeline "" { stage "" { whenCmdArg "" } } |> ignore

    pipeline "" { stage1 } |> ignore

    pipeline "" {
        cmdArgs [ "" ]
        envArgs [ "", "" ]
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
        }
    }
    |> ignore
