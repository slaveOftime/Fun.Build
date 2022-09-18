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
                return 0
            }
            )
            BuildStep(fun _ _ -> async {
                calls.Add 2
                return 0
            }
            )
            BuildStep(fun _ _ -> async {
                calls.Add 3
                return 0
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
                return 0
            }
            )
            BuildStep(fun _ _ -> async {
                calls.Add 2
                return 0
            }
            )
            timeout 10
            BuildStep(fun _ _ -> async {
                calls.Add 3
                return 0
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
