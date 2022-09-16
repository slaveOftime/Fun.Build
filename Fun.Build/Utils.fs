[<AutoOpen>]
module internal Fun.Build.Utils


module ValueOption =

    let inline defaultWithVOption (fn: unit -> 'T voption) (data: 'T voption) = if data.IsSome then data else fn ()

    let inline ofOption (data: 'T option) =
        match data with
        | Some x -> ValueSome x
        | _ -> ValueNone
