module Fun.Build.Tests.ExceptionHandlingTests

open Xunit
open Fun.Build


[<Fact>]
let ``exception handling should work for sync steps`` () =
    let mutable exn = ValueNone

    try
        shouldNotBeCalled (fun call ->
            pipeline "" {
                stage "" {
                    run (fun _ ->
                        failwith "test"
                        ()
                    )
                    run call
                }
                runImmediate
            }
        )
    with :? PipelineFailedException as ex ->
        exn <- ValueSome ex

    Assert.True exn.IsSome
    Assert.Equal("/step-0> test", exn.Value.InnerException.Message)


[<Fact>]
let ``exception handling should work for parallel steps`` () =
    let mutable exn = ValueNone

    try
        shouldNotBeCalled (fun call ->
            pipeline "" {
                stage "" {
                    paralle
                    run (fun _ -> async {
                        do! Async.Sleep 10
                        failwith "test"
                        ()
                    })
                    stage "" {
                        run (fun _ -> async {
                            do! Async.Sleep 100
                            call ()
                        })
                    }
                }
                runImmediate
            }
        )

    with :? PipelineFailedException as ex ->
        exn <- ValueSome ex

    Assert.True exn.IsSome
    Assert.Equal("/step-0> test", exn.Value.InnerException.Message)

[<Fact>]
let ``PipelineFailedException should fail fast`` () =
    let mutable exn = ValueNone

    try
        shouldNotBeCalled (fun call ->
            pipeline "" {
                stage "" {
                    continueOnStepFailure
                    run (fun _ ->
                        raise (PipelineFailedException("demo"))
                        ()
                    )
                }
                post [ stage "" { run call } ]
                runImmediate
            }
        )

    with :? PipelineFailedException as ex ->
        exn <- ValueSome ex

    Assert.True exn.IsSome
    Assert.Equal("demo", exn.Value.Message)

[<Fact>]
let ``PipelineCancelledException should fail fast`` () =
    let mutable exn = ValueNone

    try
        shouldNotBeCalled (fun call ->
            pipeline "" {
                stage "" {
                    continueOnStepFailure
                    run (fun _ ->
                        raise (PipelineCancelledException("demo"))
                        ()
                    )
                }
                post [ stage "" { run call } ]
                runImmediate
            }
        )

    with :? PipelineCancelledException as ex ->
        exn <- ValueSome ex

    Assert.True exn.IsSome
    Assert.Equal("demo", exn.Value.Message)
