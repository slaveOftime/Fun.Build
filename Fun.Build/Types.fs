namespace rec Fun.Build

open System


type PipelineCancelledException(msg: string) =
    inherit Exception(msg)


type PipelineFailedException =
    inherit Exception

    new(msg: string) = { inherit Exception(msg) }
    new(msg: string, ex: exn) = { inherit Exception(msg, ex) }
