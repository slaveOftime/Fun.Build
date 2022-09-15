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

    Assert.Equal(ValueNone, pipeline.Stages[ 0 ].TryGetCmdArgOrEnvVar "abc")
    Assert.Equal(ValueSome "e1", pipeline.Stages[ 0 ].TryGetCmdArgOrEnvVar "test1")
    Assert.Equal(ValueSome "e2", pipeline.Stages[ 0 ].TryGetCmdArgOrEnvVar "test2")
    Assert.Equal(ValueSome "c1", pipeline.Stages[ 0 ].TryGetCmdArgOrEnvVar "test3")
