[<AutoOpen>]
module Fun.Build.Tests.Utils

open Xunit


let shouldBeCalled fn =
    let mutable isCalled = false

    fn (fun _ -> isCalled <- true)

    Assert.True isCalled


let shouldNotBeCalled fn =
    let mutable isCalled = false

    fn (fun _ -> isCalled <- true)

    Assert.False isCalled
