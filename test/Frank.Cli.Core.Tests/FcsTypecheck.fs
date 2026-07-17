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

/// Compile emitted F# source to a real assembly on disk (against the given real
/// referenced assemblies) and load it. Unlike typecheckAgainstRealAssemblies, this
/// actually produces loadable IL — needed to force evaluation of a module's
/// top-level `let` bindings (their static initializers run lazily on first member
/// access), which is the only way to prove a generated module is safe to *use*,
/// not merely that it typechecks.
/// Throws on compile failure; caller decides how to report.
let compileAndLoadAssembly (emittedSrc: string) (referencedAssemblies: Assembly list) : Assembly =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tmpDir) |> ignore

    try
        // Unique assembly simple name per call: the default AssemblyLoadContext caches
        // by (name, version), so two calls both named "Generated" would return the FIRST
        // loaded assembly on the second LoadFrom instead of the freshly compiled one.
        let uniqueName = "Generated_" + Guid.NewGuid().ToString("N")
        let emittedFile = Path.Combine(tmpDir, uniqueName + ".fs")
        let outputDll = Path.Combine(tmpDir, uniqueName + ".dll")
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

        let sdkRefs =
            scriptOpts.OtherOptions
            |> Array.filter (fun o -> o.StartsWith("-r:", StringComparison.Ordinal))

        let argv =
            Array.concat
                [ [| "fsc"
                     "-o:" + outputDll
                     "--target:library"
                     "--noframework"
                     "--nowarn:57" |]
                  sdkRefs
                  extraRefs
                  [| emittedFile |] ]

        let diagnostics, exitCode = checker.Compile(argv) |> Async.RunSynchronously

        let errors =
            diagnostics
            |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

        if exitCode.IsSome || errors.Length > 0 then
            failwith $"compileAndLoadAssembly: compile failed: %A{errors}"

        Assembly.LoadFrom(outputDll)
    finally
        Directory.Delete(tmpDir, true)
