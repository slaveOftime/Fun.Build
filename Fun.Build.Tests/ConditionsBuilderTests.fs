module Fun.Build.Tests.ConditionsBuilderTests

open Xunit
open Fun.Build
open Fun.Build.Internal
open Fun.Build.StageContextExtensionsInternal
open Fun.Build.PipelineContextExtensionsInternal


[<Fact>]
let ``whenCmd should work`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenCmd { name "test1" }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "test1" ]
            stage "" {
                whenCmd { name "test1" }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "test1"; "v1" ]
            stage "" {
                whenCmd {
                    name "test1"
                    acceptValues [ "v1"; "v2" ]
                }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "-t"; "v1" ]
            stage "" {
                whenAll {
                    whenCmd {
                        name "test1"
                        alias "-t"
                        acceptValues [ "v1"; "v2" ]
                    }
                }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenCmd {
                    name "test1"
                    optional
                }
                run call
            }
            runImmediate
        }
    )


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

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenCmdArg "test1" "value" "description" true
                run call
            }
            runImmediate
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenCmdArg "test1" "value" "description" false
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "test1"; "value" ]
            stage "" {
                whenCmdArg "test1" "value" "description" false
                run call
            }
            runImmediate
        }
    )



[<Fact>]
let ``whenEnv should work`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenEnv { name "test1" }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            envVars [ "test1", "" ]
            stage "" {
                whenEnv { name "test1" }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            envVars [ "test1", "v1" ]
            stage "" {
                whenEnv {
                    name "test1"
                    acceptValues [ "v1"; "v2" ]
                }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            envVars [ "test1", "v1" ]
            stage "" {
                whenAll {
                    whenEnv {
                        name "test1"
                        acceptValues [ "v1"; "v2" ]
                    }
                }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenEnv {
                    name "test1"
                    optional
                }
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
let ``when' stage should use stage execution result as when' condition for stage`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                when' (stage "" { run (fun ctx -> 1) })
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                when' (stage "" { run (fun ctx -> 0) })
                run call
            }
            runImmediate
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenStage "" { run (fun _ -> 1) }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenStage "" { run (fun _ -> 0) }
                run call
            }
            runImmediate
        }
    )

[<Fact>]
let ``when' stage should have parent context in execution mode`` () =
    shouldBeCalled (fun call ->
        pipeline "" {
            envVars [ "ENV", "0" ]
            stage "" {
                when' (stage "" { run (fun ctx -> ctx.GetEnvVar("ENV") |> int) })
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            envVars [ "ENV", "0" ]
            stage "" {
                whenStage "" { run (fun ctx -> ctx.GetEnvVar("ENV") |> int) }
                run call
            }
            runImmediate
        }
    )

[<Fact>]
let ``when' stage should use stage execution result as when' condition for nested stage`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                stage "nested" {
                    when' (stage "" { run (fun ctx -> 1) })
                    run call
                }
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                stage "nested" {
                    when' (stage "" { run (fun ctx -> 0) })
                    run call
                }
            }
            runImmediate
        }
    )

[<Fact>]
let ``when' stage should use stage execution result as when' condition in composed when`` () =
    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenAll { when' (stage "" { run (fun ctx -> 1) }) }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenAll { when' (stage "" { run (fun ctx -> 0) }) }
                run call
            }
            runImmediate
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenAll { whenStage "" { run (fun _ -> 1) } }
                run call
            }
            runImmediate
        }
    )

    shouldBeCalled (fun call ->
        pipeline "" {
            stage "" {
                whenAll { whenStage "" { run (fun _ -> 0) } }
                run call
            }
            runImmediate
        }
    )

[<Fact>]
let ``when' stage should work in pipeline directly`` () =
    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            when' (stage "" { run (fun _ -> 1) })
            runImmediate
        }
    )
    |> ignore

    shouldBeCalled (fun call ->
        pipeline "" {
            when' (stage "" { run (fun _ -> 0) })
            stage "" { run call }
            runImmediate
        }
    )

    Assert.Throws<PipelineFailedException>(fun _ ->
        pipeline "" {
            whenStage "" { run (fun _ -> 1) }
            runImmediate
        }
    )
    |> ignore

    shouldBeCalled (fun call ->
        pipeline "" {
            whenStage "" { run (fun _ -> 0) }
            stage "" { run call }
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

    // Check situation where all the commands are optional
    let condition2 = whenAny {
        whenCmd {
            name "test1"
            acceptValues [ "v1"; "v2" ]
            optional
        }

        cmdArg "name" "value" "description" true
    }

    // All commands are optional, so it should return true when no command is provided
    { StageContext.Create "" with
        ParentContext = pipeline |> StageParent.Pipeline |> ValueSome
    }
    |> condition2.Invoke
    |> Assert.True

    // All commands are optional, so it should return true if one command is provided
    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test1"; "v1" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition2.Invoke
    |> Assert.True

    // Check situation with a mix of optional and non-optional commands
    let condition3 = whenAny {
        whenCmd {
            name "test1"
            acceptValues [ "v1"; "v2" ]
            optional
        }

        cmdArg "test2"
    }

    // Because at least one command is optional it should return true when no command is provided
    { StageContext.Create "" with
        ParentContext = pipeline |> StageParent.Pipeline |> ValueSome
    }
    |> condition3.Invoke
    |> Assert.True

    // It should return true if a non-optional command is provided
    { StageContext.Create "" with
        ParentContext = { pipeline with CmdArgs = [ "test1" ] } |> StageParent.Pipeline |> ValueSome
    }
    |> condition3.Invoke
    |> Assert.True


[<Fact>]
let ``whenAll should work`` () =
    let condition = whenAll {
        cmdArg "test1"
        envVar "test2"
        // Add optional command, this condition should always fullfill
        cmdArg "test3" "value" "description" true
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
                platformWindows
                platformLinux
                platformOSX
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


[<Fact>]
let ``acceptExitCodes should override exit codes in pipeline`` () =
    let pipeline = pipeline "main" { acceptExitCodes [| 1; 2; 3 |] }

    let codes = pipeline.AcceptableExitCodes |> Seq.toArray
    Assert.Equal<int array>([| 1; 2; 3 |], codes)

[<Fact>]
let ``acceptExitCodes should override exit codes in stage`` () =
    let stage = stage "" { acceptExitCodes [| 4; 5; 6 |] }

    let codes = stage.AcceptableExitCodes |> Seq.toArray
    Assert.Equal<int array>([| 4; 5; 6 |], codes)


[<Fact>]
let ``condition builder should follow the sequence`` () =
    let ls = System.Collections.Generic.List()

    let condition = whenAll {
        BuildStageIsActive(fun _ ->
            ls.Add(1)
            false
        )
        BuildStageIsActive(fun _ ->
            ls.Add(2)
            true
        )
        BuildStageIsActive(fun _ ->
            ls.Add(3)
            true
        )
    }

    { StageContext.Create "" with
        ParentContext = PipelineContext.Create "" |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    Assert.Equal<int>([ 1; 2; 3 ], ls)


    let ls = System.Collections.Generic.List()

    let condition = whenAll {
        cmdArg "test0"
        BuildStageIsActive(fun _ ->
            ls.Add(1)
            false
        )
        BuildStageIsActive(fun _ ->
            ls.Add(2)
            true
        )
        cmdArg "test1"
        cmdArg "test2"
        BuildStageIsActive(fun _ ->
            ls.Add(3)
            true
        )
        cmdArg "test3"
        BuildStageIsActive(fun _ ->
            ls.Add(4)
            true
        )
    }

    { StageContext.Create "" with
        ParentContext = PipelineContext.Create "" |> StageParent.Pipeline |> ValueSome
    }
    |> condition.Invoke
    |> Assert.False

    Assert.Equal<int>([ 1; 2; 3; 4 ], ls)


[<Fact>]
let ``for top level condition of stage or pipeline it should combine all condition with && rule`` () =
    shouldBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "-t" ]
            envVars [ "ENV", "" ]

            when' true
            whenCmdArg "-t"
            whenEnvVar "ENV"

            stage "" {
                when' true
                whenCmdArg "-t"
                whenEnvVar "ENV"

                run (fun _ -> call ())
            }

            runImmediate
        }
    )

    shouldNotBeCalled (fun call ->
        pipeline "" {
            cmdArgs [ "-t" ]

            when' true
            whenCmdArg "-t"

            stage "" {
                when' true
                whenCmdArg "-t"
                whenEnvVar "ENV"

                run (fun _ -> call ())
            }

            runImmediate
        }
    )
