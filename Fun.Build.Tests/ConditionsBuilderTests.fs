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
            envVars [ "test1", "" ]
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

    let pipeline = PipelineContext.Create ""

    { StageContext.Create "" with
        ParentContext = pipeline |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test1" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.True

    { StageContext.Create "" with
        ParentContext = { pipeline with EnvVars = Map.ofList [ "test2", "" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.True


[<Fact>]
let ``whenAll should work`` () =
    let condition = whenAll {
        cmdArg "test1"
        envVar "test2"
    }

    let pipeline = PipelineContext.Create ""

    { StageContext.Create "" with
        ParentContext = pipeline |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test1" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    { StageContext.Create "" with
        ParentContext = { pipeline with EnvVars = Map.ofList [ "test2", "" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    { StageContext.Create "" with
        ParentContext =
            { pipeline with
                CmdArgs = [ "test1" ]
                EnvVars = Map.ofList [ "test2", "" ]
            }
            |> StageParent.Pipeline
            |> ValueSome
    }
    |> condition.Invoke
    |> Assert.True


[<Fact>]
let ``whenNot should work`` () =
    let condition = whenNot {
        cmdArg "test1"
        envVar "test2"
    }

    let pipeline = PipelineContext.Create ""

    { StageContext.Create "" with
        ParentContext = pipeline |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.True

    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test1" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    { StageContext.Create "" with
        ParentContext = { pipeline with EnvVars = Map.ofList [ "test2", "" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    { StageContext.Create "" with
        ParentContext =
            { pipeline with
                CmdArgs = [ "test1" ]
                EnvVars = Map.ofList [ "test2", "" ]
            }
            |> StageParent.Pipeline
            |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False


[<Fact>]
let ``when compose should work`` () =
    let condition = whenAny {
        cmdArg "test1"
        whenAll {
            cmdArg "test2"
            whenNot { cmdArg "test3" }
            whenAny {
                branch "dev"
                branch "master"
            }
            whenAny {
                platformWindows
                platformLinux
            }
        }
    }

    let pipeline = PipelineContext.Create ""

    { StageContext.Create "" with
        ParentContext = pipeline |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test1" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.True

    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test2" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.True

    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test3" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False
