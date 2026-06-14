namespace Frank.LinkedData.Tests

open System
open VDS.RDF
open Frank.LinkedData

// ── Fixture: top-level GeneratedLinkedData (matches what Frank.Cli.MSBuild generates) ──

/// Top-level static class named GeneratedLinkedData with graph/jsonLdContext properties.
/// Frank.Cli.MSBuild generates this at the namespace level of the app assembly — a
/// nested (module-scoped) type would not be found by the IsPublic filter in the resolver.
[<AbstractClass; Sealed>]
type GeneratedLinkedData private () =
    static let fixtureGraph: IGraph =
        let g = new Graph()

        g.Assert(
            Triple(
                g.CreateUriNode(Uri "https://example.org/s"),
                g.CreateUriNode(Uri "https://example.org/p"),
                g.CreateUriNode(Uri "https://example.org/o")
            )
        )
        |> ignore

        g :> IGraph

    static member val graph: IGraph = fixtureGraph
    static member val jsonLdContext: string = """{"@context":["https://schema.org"]}"""

/// Top-level type missing the graph property — used to test member-resolution errors.
[<AbstractClass; Sealed>]
type GeneratedLinkedDataNoGraph private () =
    static member val jsonLdContext: string = "{}"

// ── Tests ─────────────────────────────────────────────────────────────────────

module ResolverTests =

    open System.Reflection
    open Expecto
    open Frank.LinkedData.GeneratedLinkedDataResolver

    let private testAssembly = typeof<GeneratedLinkedData>.Assembly
    let private testAssemblyArr = [| testAssembly |]

    [<Tests>]
    let tests =
        testList
            "GeneratedLinkedDataResolver"
            [ testCase "single public GeneratedLinkedData type → Ok with correct config"
              <| fun _ ->
                  let result = resolveGeneratedConfig testAssemblyArr

                  match result with
                  | Ok config ->
                      Expect.equal config.JsonLdContext GeneratedLinkedData.jsonLdContext "jsonLdContext matches"

                      Expect.equal
                          config.Graph.Triples.Count
                          GeneratedLinkedData.graph.Triples.Count
                          "graph triple count matches"
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"

              testCase "no assemblies → Error with 'no GeneratedLinkedData' and generator reference"
              <| fun _ ->
                  let result = resolveGeneratedConfig [||]

                  match result with
                  | Error msg ->
                      Expect.stringContains msg "no GeneratedLinkedData" "error identifies the problem"
                      Expect.stringContains msg "Frank.Cli.MSBuild" "error references the generator"
                  | Ok _ -> failtest "Expected Error but got Ok"

              testCase "ambiguous: same assembly listed twice → Error containing 'ambiguous'"
              <| fun _ ->
                  let doubled = [| testAssembly; testAssembly |]
                  let result = resolveGeneratedConfig doubled

                  match result with
                  | Error msg -> Expect.stringContains msg "ambiguous" "error says ambiguous"
                  | Ok _ -> failtest "Expected Error for ambiguous case but got Ok"

              testCase "type missing graph property → Error naming the missing property"
              <| fun _ ->
                  let result = resolveFromType typeof<GeneratedLinkedDataNoGraph>

                  match result with
                  | Error msg -> Expect.stringContains msg "graph" "error names the missing property"
                  | Ok _ -> failtest "Expected Error for missing graph but got Ok" ]
