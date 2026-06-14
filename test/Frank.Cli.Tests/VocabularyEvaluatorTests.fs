module Frank.Cli.Tests.VocabularyEvaluatorTests

open System
open System.IO
open System.Reflection
open Expecto
open Frank.Semantic
open Frank.Cli

// ── Assembly path helper ──────────────────────────────────────────────────────

/// Path to the Frank.Semantic.dll that this test process has already loaded.
/// Using the loaded assembly ensures the FSI session gets the SAME type identity.
let private frankSemanticDllPath () =
    let asm = Assembly.GetAssembly(typeof<VocabularyRegistry>)
    asm.Location

/// Path to the FSharp.Core.dll this process is using.
let private fsharpCoreDllPath () =
    let asm = Assembly.GetAssembly(typeof<int list>)
    asm.Location

// ── Full-fidelity vocab source (all CE operations) ───────────────────────────

/// Inline F# source that uses all seven vocabulary CE operations.
/// Defined as a file (temp) so #load works naturally.
/// Uses module syntax — F# does not allow let bindings in namespaces.
let private fullFidelitySource =
    """
module CliTestVocab

open Frank.Semantic

type MyOrder = { Total: decimal; Id: int }
type MyActivity = { Name: string }
type MyId = { Value: string }

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "wd" "https://www.wikidata.org/wiki/"
        using "schema"
        equivalentClass typeof<MyOrder> "schema:Order"
        seeAlso typeof<MyActivity> "wd:Q1"
        fieldSeeAlso typeof<MyOrder> "Total" "schema:price"
        provClass typeof<MyActivity> ProvOClass.Activity
        constrainPattern typeof<MyId> "Value" "[A-Z]{3}[0-9]+"
    }
"""

// ── Linchpin test: full-fidelity round-trip ───────────────────────────────────

[<Tests>]
let linchpinTests =
    testList
        "VocabularyEvaluator - full-fidelity registry eval"
        [ test "evalRegistry returns Ok for valid vocab source" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  Expect.isOk result "evalRegistry should succeed"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "evalRegistry: Prefixes map contains 'schema' and 'wd'" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  Expect.isTrue (reg.Prefixes.ContainsKey "schema") "schema prefix present"
                  Expect.isTrue (reg.Prefixes.ContainsKey "wd") "wd prefix present"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "evalRegistry: Using set contains 'schema'" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  Expect.isTrue (Set.contains "schema" reg.Using) "schema in Using set"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "evalRegistry: EquivalentClasses non-empty (linchpin — master would drop this)" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  let reg = Expect.wantOk result "evalRegistry should succeed"

                  Expect.isNonEmpty
                      reg.EquivalentClasses
                      "EquivalentClasses must not be empty — AST-only parsing would drop this"

                  let values = reg.EquivalentClasses |> Map.toList |> List.map snd
                  let hasOrder = values |> List.exists (fun uri -> uri.AbsoluteUri.Contains("Order"))
                  Expect.isTrue hasOrder "EquivalentClasses contains schema:Order"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "evalRegistry: SeeAlso non-empty (linchpin — master would drop this)" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  Expect.isNonEmpty reg.SeeAlso "SeeAlso must not be empty — AST-only parsing would drop this"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "evalRegistry: FieldSeeAlso non-empty (linchpin — master would drop this)" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  Expect.isNonEmpty reg.FieldSeeAlso "FieldSeeAlso must not be empty — AST-only parsing would drop this"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "evalRegistry: ProvClasses non-empty (linchpin — master would drop this)" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  Expect.isNonEmpty reg.ProvClasses "ProvClasses must not be empty — AST-only parsing would drop this"

                  let hasActivity =
                      reg.ProvClasses
                      |> Map.toList
                      |> List.exists (fun (_, v) -> v = ProvOClass.Activity)

                  Expect.isTrue hasActivity "ProvClasses contains Activity"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "evalRegistry: ConstraintPatterns non-empty (linchpin — master would drop this)" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let srcFile = Path.Combine(tmpDir, "Vocabulary.fsx")
                  File.WriteAllText(srcFile, fullFidelitySource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "CliTestVocab.registry"

                  let reg = Expect.wantOk result "evalRegistry should succeed"

                  Expect.isNonEmpty
                      reg.ConstraintPatterns
                      "ConstraintPatterns must not be empty — AST-only parsing would drop this"
              finally
                  Directory.Delete(tmpDir, true)
          } ]

// ── AT5: malformed source → Error with location ───────────────────────────────

[<Tests>]
let at5DiagnosticTests =
    testList
        "VocabularyEvaluator - AT5 error diagnostics"
        [ test "malformed source returns Error with location info" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let malformedSource =
                      """
module Bad
let x = this_does_not_exist_at_all_xyz
"""

                  let srcFile = Path.Combine(tmpDir, "Bad.fsx")
                  File.WriteAllText(srcFile, malformedSource)

                  let result =
                      VocabularyEvaluator.evalRegistry [ semanticDll; fsharpCoreDll ] [ srcFile ] "Bad.x"

                  Expect.isError result "malformed source must return Error"

                  let msg =
                      match result with
                      | Error e -> e
                      | Ok _ -> ""
                  // AT5: message must carry a location indicator (line/col or file reference)
                  let hasLocation =
                      msg.Contains("(") || msg.Contains("line") || msg.Contains("error FS")

                  Expect.isTrue hasLocation $"Error message must contain location info, got: '{msg}'"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "undefined prefix in registry raises eval error (B2 contract)" {
              let semanticDll = frankSemanticDllPath ()
              let fsharpCoreDll = fsharpCoreDllPath ()
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let undeclaredPrefixSource =
                      """
module UndeclaredPrefix
open Frank.Semantic
type MyType = { X: int }
let registry =
    vocabulary {
        equivalentClass typeof<MyType> "undeclared:Foo"
    }
"""

                  let srcFile = Path.Combine(tmpDir, "Vocab.fsx")
                  File.WriteAllText(srcFile, undeclaredPrefixSource)

                  let result =
                      VocabularyEvaluator.evalRegistry
                          [ semanticDll; fsharpCoreDll ]
                          [ srcFile ]
                          "UndeclaredPrefix.registry"

                  Expect.isError result "undeclared prefix must return Error"
              finally
                  Directory.Delete(tmpDir, true)
          } ]
