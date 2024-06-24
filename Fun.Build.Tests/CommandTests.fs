module Fun.Build.Tests.CommandTests

open Xunit
open System.Threading
open Fun.Build


[<Fact>]
let ``use cts to cancle a parallel command should work`` () =
    use cts = new CancellationTokenSource()
    let mutable count = 0
    
    pipeline "timeout" {
        stage "tasks" {
            paralle
            stage "timer" {
                whenWindows
                run "timeout /t 10" cts.Token
                run (fun _ -> count <- count + 1)
            }
            stage "timer" {
                whenNot { platformWindows }
                run "sleep 10" cts.Token
                run (fun _ -> count <- count + 1)
            }
            stage "terminator" {
                run (fun _ -> async {
                    do! Async.Sleep(1000)
                    cts.Cancel()
                })
            }
        }
        stage "" { run (fun _ -> count <- count + 1) }
        runImmediate
    }

    if count <> 2 then Assert.Fail("Should run through all stages/steps success")
