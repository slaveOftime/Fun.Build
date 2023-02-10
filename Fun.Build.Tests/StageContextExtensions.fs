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
