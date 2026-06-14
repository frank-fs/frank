namespace Frank.Discovery.Tests

open Frank.Discovery

// ── Fixtures ─────────────────────────────────────────────────────────────────

/// Top-level static class matching what Frank.Cli.MSBuild generates:
/// a public GeneratedDiscovery class with a static discoveryConfig property.
[<AbstractClass; Sealed>]
type GeneratedDiscovery private () =

    static member val discoveryConfig: DiscoveryConfig =
        { ProfileUri = "/alps/test"
          HomeRoute = "/"
          AlpsDescriptors =
            [ { Id = "TestResource"
                Type = "semantic"
                Doc = None
                Href = Some "https://schema.org/Thing" } ]
          DescribedByLinks = [] }

/// Type missing the discoveryConfig property — tests member-resolution error path.
[<AbstractClass; Sealed>]
type GeneratedDiscoveryBadMember private () =
    static member val unrelated: string = "oops"

// ── Tests ─────────────────────────────────────────────────────────────────────

module ResolverTests =

    open System.Reflection
    open Expecto
    open Frank.Discovery.GeneratedDiscoveryResolver

    let private testAssembly = typeof<GeneratedDiscovery>.Assembly
    let private testAssemblyArr = [| testAssembly |]

    [<Tests>]
    let tests =
        testList
            "GeneratedDiscoveryResolver"
            [ testCase "single public GeneratedDiscovery type → Ok with correct config"
              <| fun _ ->
                  let result = resolveGeneratedConfig testAssemblyArr

                  match result with
                  | Ok config ->
                      Expect.equal config.ProfileUri GeneratedDiscovery.discoveryConfig.ProfileUri "ProfileUri matches"
                      Expect.equal config.HomeRoute GeneratedDiscovery.discoveryConfig.HomeRoute "HomeRoute matches"

                      Expect.equal
                          config.AlpsDescriptors.Length
                          GeneratedDiscovery.discoveryConfig.AlpsDescriptors.Length
                          "descriptor count matches"
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"

              testCase "empty assembly list → Error with 'no GeneratedDiscovery'"
              <| fun _ ->
                  let result = resolveGeneratedConfig [||]

                  match result with
                  | Error msg -> Expect.stringContains msg "no GeneratedDiscovery" "error identifies the missing type"
                  | Ok _ -> failtest "Expected Error but got Ok"

              testCase "ambiguous: same assembly listed twice → Error containing 'ambiguous'"
              <| fun _ ->
                  let doubled = [| testAssembly; testAssembly |]
                  let result = resolveGeneratedConfig doubled

                  match result with
                  | Error msg -> Expect.stringContains msg "ambiguous" "error says ambiguous"
                  | Ok _ -> failtest "Expected Error for ambiguous case but got Ok" ]
