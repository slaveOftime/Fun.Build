module Fun.Build.Tests.TimeoutTests

open Xunit
open Fun.Build
open System.Diagnostics
open System.Threading.Tasks


[<Fact>]
let ``timeout should work`` () =
    shouldBeCalled (fun call ->
        pipeline "timeout" {
            timeout 1
            stage "timeout" {
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

    Assert.Throws<exn>(fun _ ->
        shouldNotBeCalled (fun call ->
            pipeline "timeout" {
                timeout 1
                stage "timeout" {
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
    )
    |> ignore

    Assert.Throws<exn>(fun _ ->
        pipeline "timeout" {
            timeout 2
            stage "timeout1" { run (Async.Sleep 1000) }
            stage "timeout2" { run (Async.Sleep 1100) }
            runImmediate
        }
    )


[<Fact>]
let ``timeoutForStage should work`` () =
    Assert.Throws<exn>(fun _ ->
        pipeline "timeoutForStage" {
            timeoutForStage 1
            stage "timeoutForStage" { run (Async.Sleep 2100) }
            runImmediate
        }
    )
    |> ignore

    Assert.Throws<exn>(fun _ ->
        pipeline "timeoutForStage" {
            timeoutForStage 2
            stage "timeoutForStage" {
                timeout 1
                run (Async.Sleep 1100)
            }
            runImmediate
        }
    )


[<Fact>]
let ``timeoutForStep should work`` () =
    Assert.Throws<exn>(fun _ ->
        shouldNotBeCalled (fun call ->
            pipeline "timeoutForStep" {
                timeoutForStep 1
                stage "timeoutForStep" {
                    run (Async.Sleep 500)
                    run (Async.Sleep 1100)
                    run call
                }
                runImmediate
            }
        )
    )
    |> ignore

    Assert.Throws<exn>(fun _ ->
        shouldNotBeCalled (fun call ->
            pipeline "timeoutForStep" {
                timeoutForStep 2
                stage "timeoutForStep" {
                    timeoutForStep 1
                    run (Async.Sleep 500)
                    run (Async.Sleep 1100)
                    run call
                }
                runImmediate
            }
        )
    )
