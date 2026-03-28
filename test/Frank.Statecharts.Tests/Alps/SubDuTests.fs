module Frank.Statecharts.Tests.Alps.SubDuTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.Classification

// ---------------------------------------------------------------------------
// #167: AlpsRoleKind and AlpsDualityKind sub-DU tests
// ---------------------------------------------------------------------------

/// Tests that the sub-DU types exist and have the correct cases.
[<Tests>]
let subDuTypeTests =
    testList
        "Sub-DU type construction (#167)"
        [
          // AlpsRoleKind cases exist
          testCase "AlpsRoleKind.ProjectedRole exists"
          <| fun () ->
              let kind: AlpsRoleKind = ProjectedRole
              Expect.equal kind ProjectedRole "ProjectedRole case"

          testCase "AlpsRoleKind.ProtocolState exists"
          <| fun () ->
              let kind: AlpsRoleKind = ProtocolState
              Expect.equal kind ProtocolState "ProtocolState case"

          // AlpsDualityKind cases exist
          testCase "AlpsDualityKind.ClientObligation exists"
          <| fun () ->
              let kind: AlpsDualityKind = ClientObligation
              Expect.equal kind ClientObligation "ClientObligation case"

          testCase "AlpsDualityKind.AdvancesProtocol exists"
          <| fun () ->
              let kind: AlpsDualityKind = AdvancesProtocol
              Expect.equal kind AdvancesProtocol "AdvancesProtocol case"

          testCase "AlpsDualityKind.DualOf exists"
          <| fun () ->
              let kind: AlpsDualityKind = DualOf
              Expect.equal kind DualOf "DualOf case"

          testCase "AlpsDualityKind.CutPoint exists"
          <| fun () ->
              let kind: AlpsDualityKind = CutPoint
              Expect.equal kind CutPoint "CutPoint case"

          // AlpsMeta uses sub-DU kinds (not string IDs)
          testCase "AlpsRole takes AlpsRoleKind, not string id"
          <| fun () ->
              let meta = AlpsRole(ProjectedRole, "admin")
              Expect.equal meta (AlpsRole(ProjectedRole, "admin")) "AlpsRole(ProjectedRole, value)"

          testCase "AlpsDuality takes AlpsDualityKind, not string id"
          <| fun () ->
              let meta = AlpsDuality(ClientObligation, "must-ack")
              Expect.equal meta (AlpsDuality(ClientObligation, "must-ack")) "AlpsDuality(ClientObligation, value)" ]

/// Tests that classifyExtension maps to sub-DU kinds.
[<Tests>]
let classifyExtensionSubDuTests =
    testList
        "classifyExtension maps to sub-DU kinds (#167)"
        [
          // Role classification
          testCase "projectedRole URI → AlpsRole(ProjectedRole, _)"
          <| fun () ->
              let ext =
                  { Id = ProjectedRoleExtId
                    Href = None
                    Value = Some "admin" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsRole(ProjectedRole, "admin"))) "projectedRole → ProjectedRole"

          testCase "protocolState URI → AlpsRole(ProtocolState, _)"
          <| fun () ->
              let ext =
                  { Id = ProtocolStateExtId
                    Href = None
                    Value = Some "authenticated" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole(ProtocolState, "authenticated")))
                  "protocolState → ProtocolState"

          testCase "bare projectedRole → AlpsRole(ProjectedRole, _) (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "projectedRole"
                    Href = None
                    Value = Some "viewer" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsRole(ProjectedRole, "viewer"))) "bare projectedRole → ProjectedRole"

          testCase "bare protocolState → AlpsRole(ProtocolState, _) (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "protocolState"
                    Href = None
                    Value = Some "running" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole(ProtocolState, "running")))
                  "bare protocolState → ProtocolState"

          // Duality classification
          testCase "clientObligation URI → AlpsDuality(ClientObligation, _)"
          <| fun () ->
              let ext =
                  { Id = ClientObligationExtId
                    Href = None
                    Value = Some "must-ack" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack")))
                  "clientObligation → ClientObligation"

          testCase "advancesProtocol URI → AlpsDuality(AdvancesProtocol, _)"
          <| fun () ->
              let ext =
                  { Id = AdvancesProtocolExtId
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(AdvancesProtocol, "true")))
                  "advancesProtocol → AdvancesProtocol"

          testCase "dualOf URI → AlpsDuality(DualOf, _)"
          <| fun () ->
              let ext =
                  { Id = DualOfExtId
                    Href = None
                    Value = Some "serverAction" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsDuality(DualOf, "serverAction"))) "dualOf → DualOf"

          testCase "cutPoint URI → AlpsDuality(CutPoint, _)"
          <| fun () ->
              let ext =
                  { Id = CutPointExtId
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsDuality(CutPoint, "true"))) "cutPoint → CutPoint"

          testCase "bare clientObligation → AlpsDuality(ClientObligation, _) (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "clientObligation"
                    Href = None
                    Value = Some "must-ack" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack")))
                  "bare clientObligation → ClientObligation"

          testCase "bare advancesProtocol → AlpsDuality(AdvancesProtocol, _) (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "advancesProtocol"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality(AdvancesProtocol, "true")))
                  "bare advancesProtocol → AdvancesProtocol"

          testCase "bare dualOf → AlpsDuality(DualOf, _) (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "dualOf"
                    Href = None
                    Value = Some "serverAction" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsDuality(DualOf, "serverAction"))) "bare dualOf → DualOf"

          testCase "bare cutPoint → AlpsDuality(CutPoint, _) (backward compat)"
          <| fun () ->
              let ext =
                  { Id = "cutPoint"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsDuality(CutPoint, "true"))) "bare cutPoint → CutPoint" ]

/// Tests that generators round-trip correctly with sub-DU kinds.
[<Tests>]
let generatorRoundTripSubDuTests =
    testList
        "Generator round-trip with sub-DU kinds (#167)"
        [ testCase "getExtAnnotations emits correct ext id for ProjectedRole"
          <| fun () ->
              let annotations =
                  [ AlpsAnnotation(AlpsRole(ProjectedRole, "admin")) ]

              let exts = Frank.Statecharts.Alps.GeneratorCommon.getExtAnnotations annotations
              Expect.hasLength exts 1 "one ext"
              let (id, href, value) = exts.[0]
              Expect.equal id ProjectedRoleExtId "id is ProjectedRoleExtId URI"
              Expect.isNone href "no href"
              Expect.equal value (Some "admin") "value preserved"

          testCase "getExtAnnotations emits correct ext id for ProtocolState"
          <| fun () ->
              let annotations =
                  [ AlpsAnnotation(AlpsRole(ProtocolState, "authenticated")) ]

              let exts = Frank.Statecharts.Alps.GeneratorCommon.getExtAnnotations annotations
              Expect.hasLength exts 1 "one ext"
              let (id, _, _) = exts.[0]
              Expect.equal id ProtocolStateExtId "id is ProtocolStateExtId URI"

          testCase "getExtAnnotations emits correct ext id for ClientObligation"
          <| fun () ->
              let annotations =
                  [ AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack")) ]

              let exts = Frank.Statecharts.Alps.GeneratorCommon.getExtAnnotations annotations
              Expect.hasLength exts 1 "one ext"
              let (id, _, _) = exts.[0]
              Expect.equal id ClientObligationExtId "id is ClientObligationExtId URI"

          testCase "getExtAnnotations emits correct ext id for AdvancesProtocol"
          <| fun () ->
              let annotations =
                  [ AlpsAnnotation(AlpsDuality(AdvancesProtocol, "true")) ]

              let exts = Frank.Statecharts.Alps.GeneratorCommon.getExtAnnotations annotations
              Expect.hasLength exts 1 "one ext"
              let (id, _, _) = exts.[0]
              Expect.equal id AdvancesProtocolExtId "id is AdvancesProtocolExtId URI"

          testCase "getExtAnnotations emits correct ext id for DualOf"
          <| fun () ->
              let annotations =
                  [ AlpsAnnotation(AlpsDuality(DualOf, "serverAction")) ]

              let exts = Frank.Statecharts.Alps.GeneratorCommon.getExtAnnotations annotations
              Expect.hasLength exts 1 "one ext"
              let (id, _, _) = exts.[0]
              Expect.equal id DualOfExtId "id is DualOfExtId URI"

          testCase "getExtAnnotations emits correct ext id for CutPoint"
          <| fun () ->
              let annotations =
                  [ AlpsAnnotation(AlpsDuality(CutPoint, "true")) ]

              let exts = Frank.Statecharts.Alps.GeneratorCommon.getExtAnnotations annotations
              Expect.hasLength exts 1 "one ext"
              let (id, _, _) = exts.[0]
              Expect.equal id CutPointExtId "id is CutPointExtId URI"

          testCase "JSON round-trip preserves sub-DU kinds"
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
                                [ AlpsAnnotation(AlpsRole(ProjectedRole, "server"))
                                  AlpsAnnotation(AlpsAvailableInStates [ "Idle" ]) ] }
                        StateDecl
                            { Identifier = Some "Active"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsRole(ProtocolState, "running"))
                                  AlpsAnnotation(AlpsDuality(DualOf, "start")) ] }
                        TransitionElement
                            { Source = "Idle"
                              Target = Some "Active"
                              Event = Some "start"
                              Guard = Some "isReady"
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe)
                                  AlpsAnnotation(AlpsDuality(ClientObligation, "must-ack")) ] } ]
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let json = Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson doc
              let parsed = Frank.Statecharts.Alps.JsonParser.parseAlpsJson json
              Expect.isEmpty parsed.Errors "parse should succeed"
              Expect.equal parsed.Document doc "round-trip preserves AST with sub-DU kinds" ]

/// Tests that TransitionExtractor correctly pattern-matches on sub-DU kinds.
[<Tests>]
let transitionExtractorSubDuTests =
    testList
        "TransitionExtractor with sub-DU kinds (#167)"
        [ testCase "resolveConstraint matches AlpsRole(ProjectedRole, _)"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ TransitionElement
                            { Source = "Idle"
                              Target = Some "Active"
                              Event = Some "start"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe)
                                  AlpsAnnotation(AlpsRole(ProjectedRole, "admin")) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let specs = Frank.Statecharts.TransitionExtractor.extract doc
              Expect.hasLength specs 1 "one transition"
              let spec = specs.[0]

              match spec.Constraint with
              | Frank.Resources.Model.RestrictedTo roles ->
                  Expect.equal roles [ "admin" ] "admin role extracted"
              | Frank.Resources.Model.Unrestricted -> failtest "expected RestrictedTo"

          testCase "extractRoles extracts from AlpsRole(ProjectedRole, _) doc annotations"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations =
                      [ AlpsAnnotation(AlpsRole(ProjectedRole, "PlayerX,PlayerO,Spectator")) ] }

              let roles = Frank.Statecharts.TransitionExtractor.extractRoles doc
              Expect.hasLength roles 3 "three roles"
              Expect.equal (roles |> List.map (fun r -> r.Name)) [ "PlayerX"; "PlayerO"; "Spectator" ] "role names"

          testCase "extractRoles ignores AlpsRole(ProtocolState, _)"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations =
                      [ AlpsAnnotation(AlpsRole(ProtocolState, "authenticated")) ] }

              let roles = Frank.Statecharts.TransitionExtractor.extractRoles doc
              Expect.isEmpty roles "ProtocolState roles not extracted as projected roles" ]
