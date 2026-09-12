[<AutoOpen>]
module Fun.Build.Tests.Utils

open System
open System.IO
open Spectre.Console
open Xunit


let shouldBeCalled fn =
    let mutable isCalled = false

    fn (fun _ -> isCalled <- true)

    Assert.True isCalled


let shouldNotBeCalled fn =
    let mutable isCalled = false

    fn (fun _ -> isCalled <- true)

    Assert.False isCalled


/// Run fn with this process' stdout and stderr redirected, and hand back what each one received.
/// xUnit runs collections in parallel, so output from other tests can land in these buffers too.
/// Assert on a marker unique to your own command rather than on the buffer as a whole.
let captureConsole (fn: unit -> unit) =
    // Deliberately not disposed. Spectre hands the writer to a cached console and goes on holding it,
    // so disposing here would make every later test die on a closed TextWriter.
    let out = new StringWriter()
    let err = new StringWriter()
    let syncOut = TextWriter.Synchronized out
    let originalOut = Console.Out
    let originalErr = Console.Error
    let originalAnsiConsole = AnsiConsole.Console

    try
        Console.SetOut syncOut
        Console.SetError(TextWriter.Synchronized err)
        // Redirecting Console.Out is not enough on its own: AnsiConsole was built around whatever
        // Console.Out was the first time it was touched, and keeps writing there.
        AnsiConsole.Console <- AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(syncOut)))
        fn ()
    finally
        AnsiConsole.Console <- originalAnsiConsole
        Console.SetOut originalOut
        Console.SetError originalErr

    out.ToString(), err.ToString()
