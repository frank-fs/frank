module Frank.Cli.Core.Tests.FcsTypecheck

open System.IO
open System
open System.Reflection

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

/// Typecheck emitted F# source against the real referenced assemblies (no stubs).
/// referencedAssemblies: the actual loaded Assembly values whose .Location paths
/// become -r: flags in the FCS project options — so real type drift breaks the gate.
/// Returns error-severity diagnostic messages (empty list = clean compile).
let typecheckAgainstRealAssemblies (emittedSrc: string) (referencedAssemblies: Assembly list) : string list =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tmpDir) |> ignore

    try
        let emittedFile = Path.Combine(tmpDir, "Generated.fs")
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

        let extraRefs =
            referencedAssemblies
            |> List.filter (fun a -> not (String.IsNullOrEmpty a.Location))
            |> List.map (fun a -> $"-r:{a.Location}")
            |> Array.ofList

        let opts =
            { scriptOpts with
                SourceFiles = [| emittedFile |]
                OtherOptions = Array.append scriptOpts.OtherOptions extraRefs }

        let results = checker.ParseAndCheckProject(opts) |> Async.RunSynchronously

        results.Diagnostics
        |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
        |> Array.map (fun d -> d.ToString())
        |> Array.toList
    finally
        Directory.Delete(tmpDir, true)

/// Typecheck three F# sources together via FCS ParseAndCheckProject.
/// src1 declares base types; src2 uses them (e.g. generated code); src3 consumes src2.
/// Returns error-severity diagnostic messages (empty list = clean compile).
let typecheckThreeSources (src1: string) (src2: string) (src3: string) : string list =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tmpDir) |> ignore

    try
        let file1 = Path.Combine(tmpDir, "Part1.fs")
        let file2 = Path.Combine(tmpDir, "Part2.fs")
        let file3 = Path.Combine(tmpDir, "Part3.fs")
        File.WriteAllText(file1, src1)
        File.WriteAllText(file2, src2)
        File.WriteAllText(file3, src3)

        let checker =
            FSharp.Compiler.CodeAnalysis.FSharpChecker.Create(keepAssemblyContents = false)

        let primaryText = FSharp.Compiler.Text.SourceText.ofString src3

        let scriptOpts, _ =
            checker.GetProjectOptionsFromScript(
                file3,
                primaryText,
                assumeDotNetFramework = false,
                useSdkRefs = true
            )
            |> Async.RunSynchronously

        let opts =
            { scriptOpts with
                SourceFiles = [| file1; file2; file3 |] }

        let results = checker.ParseAndCheckProject(opts) |> Async.RunSynchronously

        results.Diagnostics
        |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
        |> Array.map (fun d -> d.ToString())
        |> Array.toList
    finally
        Directory.Delete(tmpDir, true)
