module Frank.Validation.Tests.JsonLdLoaderTests

open System
open VDS.RDF.JsonLd
open Expecto
open Frank.Validation

/// #414 cause (a): Ontology.toGraph/toJsonLdContext ALWAYS registers rdf/rdfs/owl on every
/// served @context (Ontology.fs:110, unconditionally, regardless of ontology.Classes) — but
/// computeKnownNamespaces (EmitterShared.fs) builds knownNamespaces solely from the app's
/// declared vocabulary prefixes, so rdf/rdfs/owl are never members. JsonLdLoader.synthesizing
/// must therefore recognize all three unconditionally, without requiring the caller to pass
/// them in `namespaces` — they're structurally always in play, never app-declared vocab.
let private rdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
let private rdfsNs = "http://www.w3.org/2000/01/rdf-schema#"
let private owlNs = "http://www.w3.org/2002/07/owl#"

let private resolves (loader: JsonLdDocumentLoader) (uri: string) : bool =
    try
        loader.Invoke(Uri uri, JsonLdLoaderOptions()) |> ignore
        true
    with _ ->
        false

[<Tests>]
let tests =
    testList
        "JsonLdLoader.synthesizing (#414)"
        [ testCase "resolves rdf namespace without it being passed in namespaces"
          <| fun _ ->
              let loader = JsonLdLoader.synthesizing []
              Expect.isTrue (resolves loader rdfNs) "rdf namespace resolves unconditionally"

          testCase "resolves rdfs namespace without it being passed in namespaces"
          <| fun _ ->
              let loader = JsonLdLoader.synthesizing []
              Expect.isTrue (resolves loader rdfsNs) "rdfs namespace resolves unconditionally"

          testCase "resolves owl namespace without it being passed in namespaces"
          <| fun _ ->
              let loader = JsonLdLoader.synthesizing []
              Expect.isTrue (resolves loader owlNs) "owl namespace resolves unconditionally"

          testCase "an app-declared namespace passed in `namespaces` still resolves alongside rdf/rdfs/owl"
          <| fun _ ->
              let loader = JsonLdLoader.synthesizing [ "https://example.org/" ]
              Expect.isTrue (resolves loader "https://example.org/") "app-declared namespace still resolves"
              Expect.isTrue (resolves loader rdfNs) "rdf still resolves alongside app-declared namespaces"

          testCase "an unknown context IRI still throws (fail-closed unaffected by rdf/rdfs/owl addition)"
          <| fun _ ->
              let loader = JsonLdLoader.synthesizing [ "https://example.org/" ]
              Expect.isFalse (resolves loader "http://unknown.invalid/nope") "unknown IRI still fails closed"

          // #414 cause (b): a served @context can cite a genuinely different context-DOCUMENT
          // URL for an external vocab, not just its bare registered namespace — e.g.
          // sample/TicTacToe-v732/Program.fs's /games/{id} LinkedDataConfig.JsonLdContext
          // literally serves "https://schema.org/version/latest/schemaorg-current-https.jsonld"
          // (a real, versioned schema.org context document), while computeKnownNamespaces only
          // ever knows the bare "https://schema.org/" registry namespace — a completely
          // different string, not a mere trailing-slash variant. The loader must cover
          // whatever LinkedDataMiddleware actually put in the array. Matched by AUTHORITY
          // (VocabClassifier.isOwnedByAuthority — the SAME mechanism LinkedDataMiddleware
          // itself already uses to decide local-vs-external prefix inlining, #394), not by
          // hardcoding specific schema.org URL spellings (whack-a-mole).
          testCase "resolves a same-authority schema.org context-document URL that differs from the bare namespace"
          <| fun _ ->
              let loader = JsonLdLoader.synthesizing [ "https://schema.org/" ]
              Expect.isTrue (resolves loader "https://schema.org/") "bare namespace resolves"

              Expect.isTrue
                  (resolves loader "https://schema.org/version/latest/schemaorg-current-https.jsonld")
                  "the real, live-served schema.org versioned context-document URL resolves too, same authority as the bare namespace"

          testCase "a DIFFERENT authority is still rejected — authority matching isn't 'any URL passes'"
          <| fun _ ->
              let loader = JsonLdLoader.synthesizing [ "https://schema.org/" ]

              Expect.isFalse
                  (resolves loader "https://evil.example/fake-schema-context.jsonld")
                  "an unrelated authority must still fail closed, even if it superficially resembles a context doc"

          // #422 expert-review finding 2: two DISTINCT registered namespaces that happen to
          // share a normalized authority (host+port, path dropped) would otherwise make the
          // authority-fallback silently pick "first namespace in list order" with zero
          // diagnostic if that guess is wrong. Must fail fast at CONSTRUCTION time, naming
          // both colliding namespaces, rather than resolving requests against a guess.
          testCase
              "constructing synthesizing with two DISTINCT namespaces sharing an authority throws, naming both"
          <| fun _ ->
              let vocabA = "https://example.org/vocab-a/"
              let vocabB = "https://example.org/vocab-b/"

              try
                  JsonLdLoader.synthesizing [ vocabA; vocabB ] |> ignore
                  failtest "expected synthesizing to throw for two distinct namespaces sharing an authority"
              with ex ->
                  Expect.stringContains ex.Message vocabA "error names the first colliding namespace"
                  Expect.stringContains ex.Message vocabB "error names the second colliding namespace"

          // Regression guard: rdf/rdfs/owl are a known, deliberate, already-safe exception —
          // all three share the w3.org authority but are resolved via EXACT match in `index`,
          // never via authority-fallback. A normal real-world app vocab set (rdf/rdfs/owl +
          // one app vocab + schema.org, all on DISTINCT authorities) must still construct.
          testCase
              "synthesizing with rdf/rdfs/owl + a single app vocab + schema.org constructs successfully (no false-positive collision)"
          <| fun _ ->
              let loader =
                  JsonLdLoader.synthesizing [ "https://example.org/app-vocab/"; "https://schema.org/" ]

              Expect.isTrue (resolves loader "https://example.org/app-vocab/") "app vocab resolves"
              Expect.isTrue (resolves loader "https://schema.org/") "schema.org resolves"

              Expect.isTrue
                  (resolves loader rdfNs)
                  "rdf resolves alongside app namespaces, no false-positive collision raised" ]
