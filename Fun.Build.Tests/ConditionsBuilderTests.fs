module Fun.Build.Tests.ConditionsBuilderTests

open Xunit
open Fun.Build


[<Fact>]
let ``whenCmdArg should work`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenCmdArg "test1"
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


[<Fact>]
let ``whenEnvVar should work`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenEnvVar "test1"
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            envArgs [ "test1", "" ]
            stage "" {
                whenEnvVar "test1"
                run call
            }
            runImmediate
        }
    )


[<Fact>]
let ``whenAny should work`` () =
    let condition = whenAny {
        cmdArg "test1"
        envVar "test2"
    }

    let pipeline = PipelineContext()

    let stage = StageContext ""
    stage.PipelineContext <- ValueSome pipeline

    Assert.False(condition.Invoke (stage) ())

    pipeline.CmdArgs.Add "test1"
    Assert.True(condition.Invoke (stage) ())

    pipeline.CmdArgs.Clear()
    pipeline.EnvVars.Add("test2", "")
    Assert.True(condition.Invoke (stage) ())


[<Fact>]
let ``whenAll should work`` () =
    let condition = whenAll {
        cmdArg "test1"
        envVar "test2"
    }

    let pipeline = PipelineContext()

    let stage = StageContext ""
    stage.PipelineContext <- ValueSome pipeline

    Assert.False(condition.Invoke (stage) ())

    pipeline.CmdArgs.Add "test1"
    Assert.False(condition.Invoke (stage) ())

    pipeline.CmdArgs.Clear()
    pipeline.EnvVars.Add("test2", "")
    Assert.False(condition.Invoke (stage) ())

    pipeline.CmdArgs.Clear()
    pipeline.EnvVars.Clear()
    pipeline.CmdArgs.Add "test1"
    pipeline.EnvVars.Add("test2", "")
    Assert.True(condition.Invoke (stage) ())


[<Fact>]
let ``whenNot should work`` () =
    let condition = whenNot {
        cmdArg "test1"
        envVar "test2"
    }

    let pipeline = PipelineContext()

    let stage = StageContext ""
    stage.PipelineContext <- ValueSome pipeline

    Assert.True(condition.Invoke (stage) ())

    pipeline.CmdArgs.Add "test1"
    Assert.False(condition.Invoke (stage) ())

    pipeline.CmdArgs.Clear()
    pipeline.EnvVars.Add("test2", "")
    Assert.False(condition.Invoke (stage) ())

    pipeline.CmdArgs.Clear()
    pipeline.EnvVars.Clear()
    pipeline.CmdArgs.Add "test1"
    pipeline.EnvVars.Add("test2", "")
    Assert.False(condition.Invoke (stage) ())


[<Fact>]
let ``when compose should work`` () =
    let condition = whenAny {
        cmdArg "test1"
        whenAll {
            cmdArg "test2"
            whenNot { cmdArg "test3" }
        }
    }

    let pipeline = PipelineContext()

    let stage = StageContext ""
    stage.PipelineContext <- ValueSome pipeline

    Assert.False(condition.Invoke (stage) ())

    pipeline.CmdArgs.Add "test1"
    Assert.True(condition.Invoke (stage) ())

    pipeline.CmdArgs.Clear()
    pipeline.CmdArgs.Add("test2")
    Assert.True(condition.Invoke (stage) ())

    pipeline.CmdArgs.Clear()
    pipeline.CmdArgs.Add("test3")
    Assert.False(condition.Invoke (stage) ())
