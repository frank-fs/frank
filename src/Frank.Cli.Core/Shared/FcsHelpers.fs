namespace Frank.Cli.Core.Shared

open System.Diagnostics

[<AutoOpen>]
module FcsHelpers =

    /// Safely invoke an FCS reflection operation, returning fallback on failure.
    /// FCS APIs can throw when inspecting symbols from broken or incomplete assemblies;
    /// failures are logged at Debug level for diagnostic visibility.
    let tryFcs (fallback: 'T) (f: unit -> 'T) : 'T =
        try
            f ()
        with ex ->
            Debug.WriteLine($"FCS: {ex.Message}")
            fallback
