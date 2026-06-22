module Frank.Cli.Core.Tests.FcsTypecheck

open System.IO
open System

/// Typecheck two F# sources together via FCS ParseAndCheckProject.
/// domainSrc declares the domain types; emittedSrc uses them.
/// Returns the error-severity diagnostic messages (empty list = clean compile).
let typecheckTwoSources (domainSrc: string) (emittedSrc: string) : string list =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tmpDir) |> ignore

    try
        let domainFile = Path.Combine(tmpDir, "Domain.fs")
        let emittedFile = Path.Combine(tmpDir, "GeneratedSemanticModel.fs")
        File.WriteAllText(domainFile, domainSrc)
        File.WriteAllText(emittedFile, emittedSrc)

        let checker =
            FSharp.Compiler.CodeAnalysis.FSharpChecker.Create(keepAssemblyContents = false)

        let primaryText = FSharp.Compiler.Text.SourceText.ofString emittedSrc

        let scriptOpts, _ =
            checker.GetProjectOptionsFromScript(
                emittedFile,
                primaryText,
                assumeDotNetFramework = false,
                useSdkRefs = true
            )
            |> Async.RunSynchronously

        let opts =
            { scriptOpts with
                SourceFiles = [| domainFile; emittedFile |] }

        let results = checker.ParseAndCheckProject(opts) |> Async.RunSynchronously

        results.Diagnostics
        |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
        |> Array.map (fun d -> d.ToString())
        |> Array.toList
    finally
        Directory.Delete(tmpDir, true)
