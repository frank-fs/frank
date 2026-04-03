module Frank.Cli.Core.Tests.Unified.RoleAwareShapeTests

open System.Text.Json
open Expecto
open Frank.Resources.Model
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Unified.ProjectionPipeline

// ============================================================================
// Test Fixtures — TicTacToe with roles and transitions
// ============================================================================

let private makeField (name: string) (kind: FieldKind) : AnalyzedField =
    { Name = name
      Kind = kind
      IsRequired = true
      IsScalar = true
      Constraints = [] }

/// DU with payload cases whose names match transition event names.
let private eventType: AnalyzedType =
    { FullName = "TicTacToe.TicTacToeEvent"
      ShortName = "TicTacToeEvent"
      Kind =
        DiscriminatedUnion
            [ { Name = "MakeMove"
                Fields = [ makeField "position" (Primitive "xsd:integer") ] } ]
      GenericParameters = []
      SourceLocation = None
      IsClosed = false }

let private statechart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "Won"; "Draw" ]
      InitialStateKey = "XTurn"
      GuardNames = [ "TurnGuard" ]
      StateMetadata =
        [ "XTurn",
          { IsFinal = false
            AllowedMethods = [ "GET"; "POST" ]
            Description = None }
          "OTurn",
          { IsFinal = false
            AllowedMethods = [ "GET"; "POST" ]
            Description = None }
          "Won",
          { IsFinal = true
            AllowedMethods = [ "GET" ]
            Description = None }
          "Draw",
          { IsFinal = true
            AllowedMethods = [ "GET" ]
            Description = None } ]
        |> Map.ofList
      Roles =
        [ { Name = "PlayerX"; Description = None }
          { Name = "PlayerO"; Description = None }
          { Name = "Spectator"
            Description = None } ]
      Transitions =
        [ // PlayerX moves from XTurn
          { Event = "MakeMove"
            Source = "XTurn"
            Target = "OTurn"
            Guard = Some "TurnGuard"
            Constraint = RestrictedTo [ "PlayerX" ]
            Safety = Unsafe }
          { Event = "MakeMove"
            Source = "XTurn"
            Target = "Won"
            Guard = Some "TurnGuard"
            Constraint = RestrictedTo [ "PlayerX" ]
            Safety = Unsafe }
          { Event = "MakeMove"
            Source = "XTurn"
            Target = "Draw"
            Guard = Some "TurnGuard"
            Constraint = RestrictedTo [ "PlayerX" ]
            Safety = Unsafe }
          // PlayerO moves from OTurn
          { Event = "MakeMove"
            Source = "OTurn"
            Target = "XTurn"
            Guard = Some "TurnGuard"
            Constraint = RestrictedTo [ "PlayerO" ]
            Safety = Unsafe }
          { Event = "MakeMove"
            Source = "OTurn"
            Target = "Won"
            Guard = Some "TurnGuard"
            Constraint = RestrictedTo [ "PlayerO" ]
            Safety = Unsafe }
          { Event = "MakeMove"
            Source = "OTurn"
            Target = "Draw"
            Guard = Some "TurnGuard"
            Constraint = RestrictedTo [ "PlayerO" ]
            Safety = Unsafe } ] }

let private resource: UnifiedResource =
    { RouteTemplate = "/games/{gameId}"
      ResourceSlug = "games"
      TypeInfo = [ eventType ]
      Statechart = Some statechart
      HttpCapabilities =
        [ { Method = "GET"
            StateKey = Some "XTurn"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "POST"
            StateKey = Some "XTurn"
            LinkRelation = "makeMove"
            IsSafe = false }
          { Method = "GET"
            StateKey = Some "OTurn"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "POST"
            StateKey = Some "OTurn"
            LinkRelation = "makeMove"
            IsSafe = false }
          { Method = "GET"
            StateKey = Some "Won"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "GET"
            StateKey = Some "Draw"
            LinkRelation = "self"
            IsSafe = true } ]
      DerivedFields = ResourceModel.emptyDerivedFields }

let private baseUri = "http://example.com/alps"

let private expectedShapeUri =
    ShapeUri.buildShapeUri "TicTacToe.TicTacToeEvent.MakeMove"

// ============================================================================
// Helpers
// ============================================================================

/// Extracted descriptor info (plain data, no JsonElement references).
type DescriptorInfo =
    { Id: string
      Type: string option
      Def: string option }

/// Extract all descriptor info from ALPS JSON, recursively including nested descriptors.
let private collectDescriptors (json: string) : DescriptorInfo list =
    use doc = JsonDocument.Parse(json)
    let alps = doc.RootElement.GetProperty("alps")

    let tryStr (elem: JsonElement) (name: string) =
        match elem.TryGetProperty(name) with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let rec collect (elem: JsonElement) =
        [ match tryStr elem "id" with
          | Some id ->
              yield
                  { Id = id
                    Type = tryStr elem "type"
                    Def = tryStr elem "def" }
          | None -> ()
          match elem.TryGetProperty("descriptor") with
          | true, children when children.ValueKind = JsonValueKind.Array ->
              for child in children.EnumerateArray() do
                  yield! collect child
          | _ -> () ]

    match alps.TryGetProperty("descriptor") with
    | true, arr when arr.ValueKind = JsonValueKind.Array ->
        [ for d in arr.EnumerateArray() do
              yield! collect d ]
    | _ -> []

/// Get all transition descriptors (type = safe|unsafe|idempotent) from ALPS JSON.
let private getTransitionDescriptors (json: string) =
    collectDescriptors json
    |> List.filter (fun d ->
        match d.Type with
        | Some "safe"
        | Some "unsafe"
        | Some "idempotent" -> true
        | _ -> false)

// ============================================================================
// Tests
// ============================================================================

[<Tests>]
let roleAwareShapeTests =
    testList
        "Role-aware shape references"
        [
          // ── ShapeUri.resolveEventShapeMap ──
          testList
              "resolveEventShapeMap"
              [ testCase "maps DU case name to shape URI"
                <| fun _ ->
                    let map = ShapeUri.resolveEventShapeMap [ eventType ]
                    Expect.isTrue (map.ContainsKey "MakeMove") "Should contain MakeMove"
                    Expect.equal map.["MakeMove"] expectedShapeUri "Shape URI should match convention"

                testCase "ignores DU cases with no fields"
                <| fun _ ->
                    let enumType: AnalyzedType =
                        { FullName = "App.Status"
                          ShortName = "Status"
                          Kind =
                            DiscriminatedUnion [ { Name = "Active"; Fields = [] }; { Name = "Inactive"; Fields = [] } ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = false }

                    let map = ShapeUri.resolveEventShapeMap [ enumType ]
                    Expect.isEmpty map "Fieldless DU cases should not produce shapes"

                testCase "maps record short name to shape URI"
                <| fun _ ->
                    let recordType: AnalyzedType =
                        { FullName = "App.CreateUser"
                          ShortName = "CreateUser"
                          Kind = Record [ makeField "name" (Primitive "xsd:string") ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = true }

                    let map = ShapeUri.resolveEventShapeMap [ recordType ]
                    Expect.isTrue (map.ContainsKey "CreateUser") "Should contain CreateUser"

                    let expected = ShapeUri.buildShapeUri "App.CreateUser"
                    Expect.equal map.["CreateUser"] expected "Record shape URI should use full name"

                testCase "duplicate case names across DUs: last writer wins"
                <| fun _ ->
                    let du1: AnalyzedType =
                        { FullName = "App.Commands1"
                          ShortName = "Commands1"
                          Kind =
                            DiscriminatedUnion
                                [ { Name = "Submit"
                                    Fields = [ makeField "data" (Primitive "xsd:string") ] } ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = false }

                    let du2: AnalyzedType =
                        { FullName = "App.Commands2"
                          ShortName = "Commands2"
                          Kind =
                            DiscriminatedUnion
                                [ { Name = "Submit"
                                    Fields = [ makeField "payload" (Primitive "xsd:string") ] } ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = false }

                    let map = ShapeUri.resolveEventShapeMap [ du1; du2 ]
                    Expect.isTrue (map.ContainsKey "Submit") "Should contain Submit"
                    // Last writer wins — du2's shape URI
                    let expected = ShapeUri.buildShapeUri "App.Commands2.Submit"
                    Expect.equal map.["Submit"] expected "Duplicate key: last DU's shape URI wins" ]

          // ── ALPS def emission ──
          testList
              "ALPS def on transition descriptors"
              [ testCase "global profile emits def on POST (unsafe) descriptors"
                <| fun _ ->
                    match UnifiedAlpsGenerator.generate resource baseUri with
                    | Error errs -> failtestf "ALPS generation failed: %A" errs
                    | Ok json ->
                        let unsafeDescs =
                            getTransitionDescriptors json |> List.filter (fun d -> d.Type = Some "unsafe")

                        Expect.isGreaterThan unsafeDescs.Length 0 "Should have unsafe descriptors"

                        for d in unsafeDescs do
                            Expect.isSome d.Def $"POST descriptor '%s{d.Id}' should have def"
                            Expect.equal d.Def.Value expectedShapeUri $"def on '%s{d.Id}' should be MakeMove shape URI"

                testCase "global profile does not emit def on GET (safe) descriptors"
                <| fun _ ->
                    match UnifiedAlpsGenerator.generate resource baseUri with
                    | Error errs -> failtestf "ALPS generation failed: %A" errs
                    | Ok json ->
                        let safeDescs =
                            getTransitionDescriptors json |> List.filter (fun d -> d.Type = Some "safe")

                        Expect.isGreaterThan safeDescs.Length 0 "Should have safe descriptors"

                        for d in safeDescs do
                            Expect.isNone d.Def $"GET descriptor '%s{d.Id}' should not have def" ]

          // ── Per-role projection ──
          testList
              "Per-role projected profiles"
              [ testCase "PlayerX XTurn descriptors have def"
                <| fun _ ->
                    let result = projectResource resource baseUri
                    let playerXSlug = roleSlug "games" "PlayerX"

                    Expect.isTrue (result.RoleProfiles.ContainsKey playerXSlug) "Should have PlayerX profile"

                    let xTurnUnsafe =
                        getTransitionDescriptors result.RoleProfiles.[playerXSlug]
                        |> List.filter (fun d -> d.Id.Contains("XTurn") && d.Type = Some "unsafe")

                    Expect.isGreaterThan xTurnUnsafe.Length 0 "PlayerX should have XTurn unsafe descriptors"

                    for d in xTurnUnsafe do
                        Expect.isSome d.Def $"PlayerX XTurn descriptor '%s{d.Id}' should have def"
                        Expect.equal d.Def.Value expectedShapeUri $"def on '%s{d.Id}' should be MakeMove shape URI"

                testCase "PlayerX OTurn descriptors have no def (not PlayerX's transitions)"
                <| fun _ ->
                    let result = projectResource resource baseUri
                    let playerXSlug = roleSlug "games" "PlayerX"

                    let oTurnUnsafe =
                        getTransitionDescriptors result.RoleProfiles.[playerXSlug]
                        |> List.filter (fun d -> d.Id.Contains("OTurn") && d.Type = Some "unsafe")

                    for d in oTurnUnsafe do
                        Expect.isNone d.Def $"PlayerX OTurn descriptor '%s{d.Id}' should not have def"

                testCase "PlayerO OTurn descriptors have def"
                <| fun _ ->
                    let result = projectResource resource baseUri
                    let playerOSlug = roleSlug "games" "PlayerO"

                    Expect.isTrue (result.RoleProfiles.ContainsKey playerOSlug) "Should have PlayerO profile"

                    let oTurnUnsafe =
                        getTransitionDescriptors result.RoleProfiles.[playerOSlug]
                        |> List.filter (fun d -> d.Id.Contains("OTurn") && d.Type = Some "unsafe")

                    Expect.isGreaterThan oTurnUnsafe.Length 0 "PlayerO should have OTurn unsafe descriptors"

                    for d in oTurnUnsafe do
                        Expect.isSome d.Def $"PlayerO OTurn descriptor '%s{d.Id}' should have def"
                        Expect.equal d.Def.Value expectedShapeUri $"def on '%s{d.Id}' should be MakeMove shape URI"

                testCase "PlayerO XTurn descriptors have no def (not PlayerO's transitions)"
                <| fun _ ->
                    let result = projectResource resource baseUri
                    let playerOSlug = roleSlug "games" "PlayerO"

                    let xTurnUnsafe =
                        getTransitionDescriptors result.RoleProfiles.[playerOSlug]
                        |> List.filter (fun d -> d.Id.Contains("XTurn") && d.Type = Some "unsafe")

                    for d in xTurnUnsafe do
                        Expect.isNone d.Def $"PlayerO XTurn descriptor '%s{d.Id}' should not have def"

                testCase "Spectator profile has no def on any descriptor"
                <| fun _ ->
                    let result = projectResource resource baseUri
                    let spectatorSlug = roleSlug "games" "Spectator"

                    Expect.isTrue (result.RoleProfiles.ContainsKey spectatorSlug) "Should have Spectator profile"

                    let transitions = getTransitionDescriptors result.RoleProfiles.[spectatorSlug]

                    for d in transitions do
                        Expect.isNone d.Def $"Spectator descriptor '%s{d.Id}' should not have def"

                testCase "safe descriptors never have def in any role profile"
                <| fun _ ->
                    let result = projectResource resource baseUri

                    for KeyValue(slug, json) in result.RoleProfiles do
                        let safeDescs =
                            getTransitionDescriptors json |> List.filter (fun d -> d.Type = Some "safe")

                        for d in safeDescs do
                            Expect.isNone d.Def $"Safe descriptor '%s{d.Id}' in %s{slug} should not have def"

                testCase "Unrestricted transitions emit def in all role profiles"
                <| fun _ ->
                    // Add an Unrestricted unsafe transition from Won state (only transition there)
                    let rematchType: AnalyzedType =
                        { FullName = "TicTacToe.Rematch"
                          ShortName = "Rematch"
                          Kind = Record [ makeField "accepted" (Primitive "xsd:boolean") ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = true }

                    let broadcastStatechart =
                        { statechart with
                            Transitions =
                                statechart.Transitions
                                @ [ { Event = "Rematch"
                                      Source = "Won"
                                      Target = "XTurn"
                                      Guard = None
                                      Constraint = Unrestricted
                                      Safety = Unsafe } ] }

                    let broadcastResource =
                        { resource with
                            TypeInfo = resource.TypeInfo @ [ rematchType ]
                            Statechart = Some broadcastStatechart
                            HttpCapabilities =
                                resource.HttpCapabilities
                                @ [ { Method = "POST"
                                      StateKey = Some "Won"
                                      LinkRelation = "rematch"
                                      IsSafe = false } ] }

                    let result = projectResource broadcastResource baseUri
                    let rematchShapeUri = ShapeUri.buildShapeUri "TicTacToe.Rematch"

                    // All roles should see the Unrestricted transition with def
                    for role in [ "PlayerX"; "PlayerO"; "Spectator" ] do
                        let slug = roleSlug "games" role

                        Expect.isTrue (result.RoleProfiles.ContainsKey slug) $"%s{role} should have profile"

                        let wonUnsafe =
                            getTransitionDescriptors result.RoleProfiles.[slug]
                            |> List.filter (fun d -> d.Type = Some "unsafe" && d.Id.Contains("Won"))

                        Expect.isGreaterThan wonUnsafe.Length 0 $"%s{role} should have Won unsafe descriptors"

                        for d in wonUnsafe do
                            Expect.equal
                                d.Def
                                (Some rematchShapeUri)
                                $"%s{role} descriptor '%s{d.Id}' should have Rematch def" ] ]
