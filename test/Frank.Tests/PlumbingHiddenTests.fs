module Frank.Tests.PlumbingHiddenTests

/// #392: codegen/interop plumbing must not be part of any consumer's compile-time
/// surface. Proves each named plumbing type is either absent from its assembly's
/// public surface (asm.GetType returns null) or present but not IsPublic (internal).
/// Uses reflection only — never `typeof<...>` on a plumbing type, since that would
/// require compile-time access to the very thing this test proves is hidden.
open System.Reflection
open Expecto

/// A stable, intentionally-public marker type used only to resolve each assembly.
let private frankAsm = typeof<Frank.Builder.ResourceBuilder>.Assembly
let private semanticAsm = typeof<Frank.Semantic.VocabularyRegistry>.Assembly
let private discoveryAsm = typeof<Frank.Discovery.AlpsDescriptor>.Assembly
let private validationAsm = typeof<Frank.Validation.ValidationConfig>.Assembly
let private linkedDataAsm = typeof<Frank.LinkedData.LinkedDataConfig>.Assembly
let private provenanceAsm = typeof<Frank.Provenance.ProvenanceConfig>.Assembly

/// Assert that `fullName` is NOT resolvable as a public type on `asm`:
/// either the type does not exist (hidden entirely by .fsi omission), or it
/// exists but is not public (hidden via `internal` + InternalsVisibleTo).
let private assertPlumbingHidden (asm: Assembly) (fullName: string) : unit =
    match asm.GetType(fullName) with
    | null -> ()
    | t -> Expect.isFalse t.IsPublic $"{fullName} must not be a public type on {asm.GetName().Name}"

[<Tests>]
let plumbingHiddenTests =
    testList
        "Plumbing hidden from consumer compile-time surface (#392)"
        [ test "Frank.GeneratedModuleReflection is not public" {
              assertPlumbingHidden frankAsm "Frank.GeneratedModuleReflection"
          }

          test "Frank.Semantic.RdfSerialization is not public" {
              assertPlumbingHidden semanticAsm "Frank.Semantic.RdfSerialization"
          }

          test "Frank.Discovery.AlpsSerializer is not public" {
              assertPlumbingHidden discoveryAsm "Frank.Discovery.AlpsSerializer"
          }

          test "Frank.Discovery.JsonHomeSerializer is not public" {
              assertPlumbingHidden discoveryAsm "Frank.Discovery.JsonHomeSerializer"
          }

          test "Frank.Discovery.GeneratedDiscoveryResolver is not public" {
              assertPlumbingHidden discoveryAsm "Frank.Discovery.GeneratedDiscoveryResolver"
          }

          test "Frank.Validation.GeneratedValidationResolver is not public" {
              assertPlumbingHidden validationAsm "Frank.Validation.GeneratedValidationResolver"
          }

          test "Frank.LinkedData.GeneratedLinkedDataResolver is not public" {
              assertPlumbingHidden linkedDataAsm "Frank.LinkedData.GeneratedLinkedDataResolver"
          }

          test "Frank.Provenance.GeneratedProvenanceResolver is not public" {
              assertPlumbingHidden provenanceAsm "Frank.Provenance.GeneratedProvenanceResolver"
          } ]
