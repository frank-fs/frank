namespace Frank.Validation.Tests

open System
open VDS.RDF.Shacl
open Frank.Semantic
open Frank.Validation

// ── Fixture: top-level GeneratedValidation (matches what Frank.Cli.MSBuild generates) ──

/// Top-level static class named GeneratedValidation with a shapesGraph property.
/// Frank.Cli.MSBuild generates this at the namespace level of the app assembly.
[<AbstractClass; Sealed>]
type GeneratedValidation private () =
    static let fixtureGraph: ShapesGraph =
        Shapes.toShapesGraph
            [ EnumShape(
                  Uri "https://example.org/Status",
                  { Head = Uri "https://example.org/Active"
                    Tail = [] }
              ) ]

    static member val shapesGraph: ShapesGraph = fixtureGraph
    static member val knownNamespaces: string[] = [| "https://example.org/" |]

/// Type missing the shapesGraph property — tests member-resolution errors.
[<AbstractClass; Sealed>]
type GeneratedValidationNoGraph private () =
    static member val unrelated: string = "nothing"

// ── Tests ──

module ResolverTests =

    open System.Reflection
    open Expecto
    open Frank.Validation.GeneratedValidationResolver

    let private testAssembly = typeof<GeneratedValidation>.Assembly
    let private testAssemblyArr = [| testAssembly |]

    [<Tests>]
    let tests =
        testList
            "GeneratedValidationResolver"
            [ testCase "single public GeneratedValidation type → Ok with non-null ShapesGraph"
              <| fun _ ->
                  let result = resolveGeneratedConfig testAssemblyArr

                  match result with
                  | Ok config -> Expect.isNotNull (box config.Shapes) "ShapesGraph is not null"
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"

              testCase "no assemblies → Error with 'no GeneratedValidation'"
              <| fun _ ->
                  let result = resolveGeneratedConfig [||]

                  match result with
                  | Error msg -> Expect.stringContains msg "no GeneratedValidation" "error identifies the problem"
                  | Ok _ -> failtest "Expected Error but got Ok"

              testCase "ambiguous: same assembly listed twice → Error containing 'ambiguous'"
              <| fun _ ->
                  let doubled = [| testAssembly; testAssembly |]
                  let result = resolveGeneratedConfig doubled

                  match result with
                  | Error msg -> Expect.stringContains msg "ambiguous" "error says ambiguous"
                  | Ok _ -> failtest "Expected Error for ambiguous case but got Ok"

              testCase "type missing shapesGraph property → Error naming the missing property"
              <| fun _ ->
                  let result = resolveFromType typeof<GeneratedValidationNoGraph>

                  match result with
                  | Error msg -> Expect.stringContains msg "shapesGraph" "error names the missing property"
                  | Ok _ -> failtest "Expected Error for missing shapesGraph but got Ok"

              testCase "loader is synthesizing — unknown @context IRI throws (fail-closed)"
              <| fun _ ->
                  let result = resolveFromType typeof<GeneratedValidation>

                  match result with
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"
                  | Ok config ->
                      let unknownUri = System.Uri("http://example.com/unknown")
                      let opts = VDS.RDF.JsonLd.JsonLdLoaderOptions()
                      Expect.throws (fun () -> config.ContextLoader.Invoke(unknownUri, opts) |> ignore) "unknown context IRI must throw (fail-closed)" ]
