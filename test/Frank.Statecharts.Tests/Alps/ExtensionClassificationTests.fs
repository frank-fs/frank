module Frank.Statecharts.Tests.Alps.ExtensionClassificationTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.Classification

// ---------------------------------------------------------------------------
// classifyExtension: maps ParsedExtension → typed AlpsMeta DU cases
// ---------------------------------------------------------------------------

[<Tests>]
let classifyExtensionTests =
    testList
        "ALPS extension classification"
        [
          // Guard extensions
          testCase "guard extension → AlpsGuardExt"
          <| fun () ->
              let ext =
                  { Id = "guard"
                    Href = None
                    Value = Some "emailValid" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsGuardExt("emailValid", None))) "guard → AlpsGuardExt"

          testCase "guard extension without value → AlpsGuardExt empty"
          <| fun () ->
              let ext =
                  { Id = "guard"
                    Href = None
                    Value = None }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsGuardExt("", None))) "guard with no value → AlpsGuardExt \"\""

          // Role extensions
          testCase "projectedRole → AlpsRole"
          <| fun () ->
              let ext =
                  { Id = ProjectedRoleExtId
                    Href = None
                    Value = Some "admin" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsRole(ProjectedRole, "admin", None))) "projectedRole → AlpsRole"

          testCase "protocolState → AlpsRole"
          <| fun () ->
              let ext =
                  { Id = ProtocolStateExtId
                    Href = None
                    Value = Some "authenticated" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole(ProtocolState, "authenticated", None)))
                  "protocolState → AlpsRole"

          // Duality extensions
          testCase "clientObligation → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = ClientObligationExtId
                    Href = None
                    Value = Some "must-ack" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack", None)))
                  "clientObligation → AlpsDuality"

          testCase "advancesProtocol → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = AdvancesProtocolExtId
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(AdvancesProtocol, "true", None)))
                  "advancesProtocol → AlpsDuality"

          testCase "dualOf → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = DualOfExtId
                    Href = None
                    Value = Some "serverAction" }

              let result = classifyExtension ext

              Expect.equal result (AlpsAnnotation(AlpsDuality(DualOf, "serverAction", None))) "dualOf → AlpsDuality"

          testCase "cutPoint → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = CutPointExtId
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal result (AlpsAnnotation(AlpsDuality(CutPoint, "true", None))) "cutPoint → AlpsDuality"

          // AvailableInStates extension
          testCase "availableInStates → AlpsAvailableInStates"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "XTurn,OTurn" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsAvailableInStates([ "XTurn"; "OTurn" ], None)))
                  "availableInStates → AlpsAvailableInStates"

          testCase "availableInStates single state"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "Won" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsAvailableInStates([ "Won" ], None))) "single state"

          testCase "availableInStates trims whitespace around entries"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "XTurn, OTurn , Won" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsAvailableInStates([ "XTurn"; "OTurn"; "Won" ], None)))
                  "whitespace trimmed"

          testCase "availableInStates ignores empty entries from double commas"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "XTurn,,OTurn" }

              let result = classifyExtension ext

              Expect.equal result (AlpsAnnotation(AlpsAvailableInStates([ "XTurn"; "OTurn" ], None))) "empty entries removed"

          // Fallback: unknown extension → AlpsExtension (backward compat)
          testCase "unknown extension → AlpsExtension fallback"
          <| fun () ->
              let ext =
                  { Id = "author"
                    Href = Some "http://example.com"
                    Value = Some "amundsen" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsExtension("author", Some "http://example.com", Some "amundsen")))
                  "unknown → AlpsExtension"

          // -----------------------------------------------------------------
          // #165: Extension IDs use namespaced HTTPS URIs
          // -----------------------------------------------------------------

          // --- New URI IDs classify correctly ---
          testCase "guard URI → AlpsGuardExt"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/guard"
                    Href = None
                    Value = Some "isReady" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsGuardExt("isReady", None))) "guard URI → AlpsGuardExt"

          testCase "projectedRole URI → AlpsRole"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/projectedRole"
                    Href = None
                    Value = Some "admin" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole(ProjectedRole, "admin", None)))
                  "projectedRole URI → AlpsRole"

          testCase "protocolState URI → AlpsRole"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/protocolState"
                    Href = None
                    Value = Some "authenticated" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole(ProtocolState, "authenticated", None)))
                  "protocolState URI → AlpsRole"

          testCase "availableInStates URI → AlpsAvailableInStates"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/availableInStates"
                    Href = None
                    Value = Some "XTurn,OTurn" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsAvailableInStates([ "XTurn"; "OTurn" ], None)))
                  "availableInStates URI → AlpsAvailableInStates"

          testCase "clientObligation URI → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/clientObligation"
                    Href = None
                    Value = Some "must-ack" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack", None)))
                  "clientObligation URI → AlpsDuality"

          testCase "advancesProtocol URI → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/advancesProtocol"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(AdvancesProtocol, "true", None)))
                  "advancesProtocol URI → AlpsDuality"

          testCase "dualOf URI → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/dualOf"
                    Href = None
                    Value = Some "serverAction" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(DualOf, "serverAction", None)))
                  "dualOf URI → AlpsDuality"

          testCase "cutPoint URI → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "https://frank-fs.github.io/alps-ext/cutPoint"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(CutPoint, "true", None)))
                  "cutPoint URI → AlpsDuality"

          // --- Backward compat: old bare names still classify correctly ---
          testCase "bare guard → AlpsGuardExt (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "guard"
                    Href = None
                    Value = Some "emailValid" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsGuardExt("emailValid", None))) "bare guard still works"

          testCase "bare projectedRole → AlpsRole with canonical URI (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "projectedRole"
                    Href = None
                    Value = Some "admin" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole(ProjectedRole, "admin", None)))
                  "bare projectedRole normalizes to URI"

          testCase "bare protocolState → AlpsRole with canonical URI (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "protocolState"
                    Href = None
                    Value = Some "running" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole(ProtocolState, "running", None)))
                  "bare protocolState normalizes to URI"

          testCase "bare availableInStates → AlpsAvailableInStates (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "Idle" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsAvailableInStates([ "Idle" ], None)))
                  "bare availableInStates still works"

          testCase "bare clientObligation → AlpsDuality with canonical URI (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "clientObligation"
                    Href = None
                    Value = Some "must-ack" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack", None)))
                  "bare clientObligation normalizes to URI"

          testCase "bare advancesProtocol → AlpsDuality with canonical URI (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "advancesProtocol"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(AdvancesProtocol, "true", None)))
                  "bare advancesProtocol normalizes to URI"

          testCase "bare dualOf → AlpsDuality with canonical URI (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "dualOf"
                    Href = None
                    Value = Some "serverAction" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(DualOf, "serverAction", None)))
                  "bare dualOf normalizes to URI"

          testCase "bare cutPoint → AlpsDuality with canonical URI (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "cutPoint"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal result (AlpsAnnotation(AlpsDuality(CutPoint, "true", None))) "bare cutPoint normalizes to URI"

          // --- Constants are HTTPS URIs ---
          testCase "GuardExtId is an HTTPS URI"
          <| fun () ->
              Expect.isTrue
                  (GuardExtId.StartsWith("https://frank-fs.github.io/alps-ext/"))
                  "GuardExtId should be an HTTPS URI"

          testCase "all ext constants are HTTPS URIs"
          <| fun () ->
              let allConstants =
                  [ GuardExtId
                    ProjectedRoleExtId
                    ProtocolStateExtId
                    AvailableInStatesExtId
                    ClientObligationExtId
                    AdvancesProtocolExtId
                    DualOfExtId
                    CutPointExtId ]

              for c in allConstants do
                  Expect.isTrue
                      (c.StartsWith("https://frank-fs.github.io/alps-ext/"))
                      $"Constant '{c}' should be an HTTPS URI" ]

// ---------------------------------------------------------------------------
// buildStateAnnotations uses classifyExtension for typed extensions
// ---------------------------------------------------------------------------

[<Tests>]
let buildStateAnnotationsTypedTests =
    testList
        "buildStateAnnotations with typed extensions"
        [ testCase "state with role extension gets AlpsRole annotation"
          <| fun () ->
              let descriptor =
                  { Id = Some "XTurn"
                    Type = Some "semantic"
                    Href = None
                    Def = None
                    ReturnType = None
                    DocFormat = None
                    DocValue = None
                    Children = []
                    Extensions =
                      [ { Id = ProjectedRoleExtId
                          Href = None
                          Value = Some "PlayerX" } ]
                    Links = [] }

              let annotations = buildStateAnnotations descriptor

              let hasRole =
                  annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsRole(ProjectedRole, "PlayerX", _)) -> true
                      | _ -> false)

              Expect.isTrue hasRole "should contain AlpsRole annotation"

          testCase "state with unknown extension gets AlpsExtension annotation"
          <| fun () ->
              let descriptor =
                  { Id = Some "home"
                    Type = Some "semantic"
                    Href = None
                    Def = None
                    ReturnType = None
                    DocFormat = None
                    DocValue = None
                    Children = []
                    Extensions =
                      [ { Id = "author"
                          Href = None
                          Value = Some "test" } ]
                    Links = [] }

              let annotations = buildStateAnnotations descriptor

              let hasExt =
                  annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsExtension("author", None, Some "test")) -> true
                      | _ -> false)

              Expect.isTrue hasExt "should contain AlpsExtension annotation" ]

// ---------------------------------------------------------------------------
// buildTransitionAnnotations uses classifyExtension for typed extensions
// ---------------------------------------------------------------------------

[<Tests>]
let buildTransitionAnnotationsTypedTests =
    testList
        "buildTransitionAnnotations with typed extensions"
        [ testCase "transition with duality extension gets AlpsDuality annotation"
          <| fun () ->
              let resolved =
                  { Id = Some "makeMove"
                    Type = Some "unsafe"
                    Href = None
                    Def = None
                    ReturnType = Some "#OTurn"
                    DocFormat = None
                    DocValue = None
                    Children = []
                    Extensions =
                      [ { Id = GuardExtId
                          Href = None
                          Value = Some "role=PlayerX" }
                        { Id = ClientObligationExtId
                          Href = None
                          Value = Some "must-ack" } ]
                    Links = [] }

              let originalChild =
                  { Id = Some "makeMove"
                    Type = Some "unsafe"
                    Href = None
                    Def = None
                    ReturnType = Some "#OTurn"
                    DocFormat = None
                    DocValue = None
                    Children = []
                    Extensions = resolved.Extensions
                    Links = [] }

              let annotations =
                  buildTransitionAnnotations AlpsTransitionKind.Unsafe originalChild resolved

              // Guard should be excluded (handled by extractGuard)
              let hasDuality =
                  annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack", _)) -> true
                      | _ -> false)

              let hasGuard =
                  annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsGuardExt _) -> true
                      | _ -> false)

              Expect.isTrue hasDuality "should contain AlpsDuality annotation"
              Expect.isFalse hasGuard "guard should be excluded from transition annotations" ]

// ---------------------------------------------------------------------------
// #165: Generated ext elements contain full URIs, not bare names
// ---------------------------------------------------------------------------

[<Tests>]
let generatedExtUriTests =
    testList
        "Generated ext elements use HTTPS URIs (#165)"
        [ testCase "generated JSON contains full URI for guard ext"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Idle"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "Idle"
                              Target = Some "Active"
                              Event = Some "start"
                              Guard = Some "isReady"
                              GuardHref = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson doc
              Expect.stringContains json "https://frank-fs.github.io/alps-ext/guard" "generated JSON uses guard URI"
              Expect.isFalse (json.Contains("\"id\": \"guard\"")) "generated JSON does not use bare guard id"

          testCase "generated JSON contains full URI for projectedRole ext"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Idle"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsRole(ProjectedRole, "server", None)) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson doc

              Expect.stringContains
                  json
                  "https://frank-fs.github.io/alps-ext/projectedRole"
                  "generated JSON uses projectedRole URI"

          testCase "generated XML contains full URI for guard ext"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Idle"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "Idle"
                              Target = Some "Active"
                              Event = Some "start"
                              Guard = Some "isReady"
                              GuardHref = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = Frank.Statecharts.Alps.XmlGenerator.generateAlpsXml doc
              Expect.stringContains xml "https://frank-fs.github.io/alps-ext/guard" "generated XML uses guard URI"

          testCase "round-trip: parse generated ALPS with URIs, regenerate, compare"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Idle"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsRole(ProjectedRole, "server", None))
                                  AlpsAnnotation(AlpsAvailableInStates([ "Idle" ], None)) ] }
                        StateDecl
                            { Identifier = Some "Active"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsRole(ProtocolState, "running", None))
                                  AlpsAnnotation(AlpsDuality(DualOf, "start", None)) ] }
                        TransitionElement
                            { Source = "Idle"
                              Target = Some "Active"
                              Event = Some "start"
                              Guard = Some "isReady"
                              GuardHref = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe)
                                  AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack", None)) ] } ]
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let json1 = Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson doc
              let parsed1 = Frank.Statecharts.Alps.JsonParser.parseAlpsJson json1
              Expect.isEmpty parsed1.Errors "first parse should succeed"

              let json2 = Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson parsed1.Document
              let parsed2 = Frank.Statecharts.Alps.JsonParser.parseAlpsJson json2
              Expect.isEmpty parsed2.Errors "second parse should succeed"

              Expect.equal parsed2.Document parsed1.Document "round-trip preserves AST"

          testCase "backward compat: parse ALPS with old bare names, verify classification"
          <| fun () ->
              // JSON with old-style bare ext ids
              let oldJson =
                  """{"alps":{"version":"1.0","descriptor":[{"id":"Idle","type":"semantic","ext":[{"id":"projectedRole","value":"server"},{"id":"availableInStates","value":"Idle"}],"descriptor":[{"id":"start","type":"unsafe","rt":"#Active","ext":[{"id":"guard","value":"isReady"},{"id":"clientObligation","value":"must-ack"}]}]}]}}"""

              let parsed = Frank.Statecharts.Alps.JsonParser.parseAlpsJson oldJson
              Expect.isEmpty parsed.Errors "parse of old-style bare names should succeed"

              // Verify the state has the correct role annotation with canonical URI
              let states =
                  parsed.Document.Elements
                  |> List.choose (fun el ->
                      match el with
                      | StateDecl s -> Some s
                      | _ -> None)

              let idle = states |> List.find (fun s -> s.Identifier = Some "Idle")

              let hasRoleWithUri =
                  idle.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsRole(ProjectedRole, "server", _)) -> true
                      | _ -> false)

              Expect.isTrue hasRoleWithUri "bare projectedRole should normalize to URI in parsed AST"

              // Verify the generated output uses full URIs
              let generated =
                  Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson parsed.Document

              Expect.stringContains
                  generated
                  "https://frank-fs.github.io/alps-ext/projectedRole"
                  "regenerated from old bare names emits full URIs"

              Expect.stringContains
                  generated
                  "https://frank-fs.github.io/alps-ext/guard"
                  "regenerated from old bare names emits full guard URI"

              Expect.stringContains
                  generated
                  "https://frank-fs.github.io/alps-ext/clientObligation"
                  "regenerated from old bare names emits full clientObligation URI"

              Expect.stringContains
                  generated
                  "https://frank-fs.github.io/alps-ext/availableInStates"
                  "regenerated from old bare names emits full availableInStates URI" ]
