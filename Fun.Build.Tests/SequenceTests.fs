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
            run (fun _ -> calls.Add 2)
            run (fun _ -> calls.Add 3)
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3 ], calls)

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
            BuildStep(fun _ _ -> async {
                calls.Add 1
                return Ok()
            }
            )
            BuildStep(fun _ _ -> async {
                calls.Add 2
                return Ok()
            }
            )
            BuildStep(fun _ _ -> async {
                calls.Add 3
                return Ok()
            }
            )
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3 ], calls)

    calls.Clear()
    pipeline "" {
        stage "" {
            timeout 10
            BuildStep(fun _ _ -> async {
                calls.Add 1
                return Ok()
            }
            )
            BuildStep(fun _ _ -> async {
                calls.Add 2
                return Ok()
            }
            )
            timeout 10
            BuildStep(fun _ _ -> async {
                calls.Add 3
                return Ok()
            }
            )
            timeout 10
        }
        runImmediate
    }
    Assert.Equal<int>([ 1; 2; 3 ], calls)



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
