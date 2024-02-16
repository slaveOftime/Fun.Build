#r "nuget: Fun.Result, 0.2.1"
#r "nuget: Spectre.Console, 0.46.0"
#r "Fun.Build/bin/Debug/netstandard2.0/Fun.Build.dll"

open Fun.Build

pipeline "demo" {
    stage "continueation" {
        paralle
        continueOnStepFailure
        run (fun _ ->
            failwith "error1"
            ()
        )
        stage "deep" {
            run (fun _ ->
                failwith "error2"
                ()
            )
        }
    }
    stage "con2" {
        continueStepsOnFailure
        run (Async.Sleep 5000)
        run (fun _ ->
            failwith "error3"
            ()
        )
    }
    runIfOnlySpecified
}

tryPrintPipelineCommandHelp ()
