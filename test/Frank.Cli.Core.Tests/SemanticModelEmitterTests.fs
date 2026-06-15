module Frank.Cli.Core.Tests.SemanticModelEmitterTests

open System
open System.IO
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

// ── Test fixtures ─────────────────────────────────────────────────────────────

let private schemaPrefix = Uri("https://schema.org/")

let private probeRegistry: VocabularyRegistry =
    { VocabularyRegistry.empty with
        Prefixes = Map.ofList [ "schema", schemaPrefix ] }

let private probeLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                Hash = "sha256:test" } ]
      Mappings =
        [ { FSharpType = "Probe.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Fields = [] }
          { FSharpType = "Probe.Holder`1"
            Iri = Some "schema:Holder"
            Confidence = 0.9
            Source = Convention
            Status = Confirmed
            Alternates = []
            Fields = [] }
          { FSharpType = "Probe.Unmapped"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Fields = [] } ] }

let private noClassLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
      Vocabularies = Map.empty
      Mappings =
        [ { FSharpType = "Probe.NoClass"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Fields = [] } ] }

// ── Helpers ───────────────────────────────────────────────────────────────────

let private unwrapOk (r: Result<string, string>) : string =
    match r with
    | Ok s -> s
    | Error e -> failwith $"Expected Ok but got Error: {e}"

// ── FCS typecheck helper ──────────────────────────────────────────────────────

/// Typecheck two source strings together via FCS ParseAndCheckProject.
/// domainSrc is compiled first (declares the types), emittedSrc second (uses them).
/// Returns the error diagnostic messages (empty list means no errors).
let private typecheckTwoSources (domainSrc: string) (emittedSrc: string) : string list =
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

// ── Probe domain sources ──────────────────────────────────────────────────────

let private domainSrc =
    """
namespace Probe

type Game = { Id: int }
type Holder<'T> = { Value: 'T }
type Unmapped = { Tag: string }
"""

let private domainSrcRenamed =
    """
namespace Probe

type GameX = { Id: int }
type Holder<'T> = { Value: 'T }
"""

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let duShapeTests =
    testList
        "SemanticModelEmitter — DU shape"
        [ test "emitted source contains 'type SemanticResource ='" {
              let src = SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock
              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "type SemanticResource =" "DU type declaration"
          }

          test "DU has case per class-mapped resource only" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              Expect.stringContains src "| Game" "Game case present"
              Expect.stringContains src "| Holder" "Holder case present"
              Expect.isFalse (src.Contains("| Unmapped")) "Unmapped has no ClassIri — no case"
          } ]

[<Tests>]
let iriMatchTests =
    testList
        "SemanticModelEmitter — iri match arms"
        [ test "iri arm for Game emits System.Uri constructor" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              Expect.stringContains
                  src
                  "| Game -> System.Uri(\"https://schema.org/Game\")"
                  "Game iri arm uses System.Uri"
          }

          test "iri arm for Holder emits System.Uri constructor" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              Expect.stringContains
                  src
                  "| Holder -> System.Uri(\"https://schema.org/Holder\")"
                  "Holder iri arm uses System.Uri"
          }

          test "iri function return type annotation is System.Uri" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              Expect.stringContains src "let iri (r: SemanticResource) : System.Uri =" "iri return type is System.Uri"
          } ]

[<Tests>]
let clrTypeMatchTests =
    testList
        "SemanticModelEmitter — clrType match arms"
        [ test "record (arity=0) uses typeof<Probe.Game>" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              Expect.stringContains src "| Game -> typeof<Probe.Game>" "Game clrType arm"
          }

          test "generic (arity=1) uses typedefof<Probe.Holder<_>>" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              Expect.stringContains src "| Holder -> typedefof<Probe.Holder<_>>" "Holder clrType arm"
          } ]

[<Tests>]
let zeroResourcesTests =
    testList
        "SemanticModelEmitter — zero class-mapped resources"
        [ test "lock with no class-mapped resources returns Error" {
              let result = SemanticModelEmitter.emit "Probe.Generated" probeRegistry noClassLock
              Expect.isError result "no class-mapped resources must return Error"

              let msg =
                  match result with
                  | Error e -> e
                  | Ok _ -> ""

              Expect.stringContains msg "no class-mapped resources" "error message names the reason"
          } ]

[<Tests>]
let antiDriftHeaderTests =
    testList
        "SemanticModelEmitter — anti-drift guard header"
        [ test "emitted module has auto-generated header comment" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              Expect.stringContains src "<auto-generated>" "header contains <auto-generated> marker"
              Expect.stringContains src "Anti-drift guard" "header explains purpose"
          } ]

[<Tests>]
let antiDriftTests =
    testList
        "SemanticModelEmitter — anti-drift (FCS typecheck)"
        [ test "emitted module + matching domain → zero error diagnostics" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              let errors = typecheckTwoSources domainSrc src
              Expect.isEmpty errors $"must compile clean; got errors: {errors}"
          }

          test "emitted module + renamed domain (Game→GameX) → has error diagnostics" {
              let src =
                  unwrapOk (SemanticModelEmitter.emit "Probe.Generated" probeRegistry probeLock)

              let errors = typecheckTwoSources domainSrcRenamed src
              Expect.isNonEmpty errors "renaming Game→GameX must break the emitted typeof<Probe.Game>"
          } ]
