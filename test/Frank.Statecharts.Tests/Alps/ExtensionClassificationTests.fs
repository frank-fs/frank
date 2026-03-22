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
              Expect.equal result (AlpsAnnotation(AlpsGuardExt "emailValid")) "guard → AlpsGuardExt"

          testCase "guard extension without value → AlpsGuardExt empty"
          <| fun () ->
              let ext =
                  { Id = "guard"
                    Href = None
                    Value = None }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsGuardExt "")) "guard with no value → AlpsGuardExt \"\""

          // Role extensions
          testCase "projectedRole → AlpsRole"
          <| fun () ->
              let ext =
                  { Id = "projectedRole"
                    Href = None
                    Value = Some "admin" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsRole("projectedRole", "admin"))) "projectedRole → AlpsRole"

          testCase "protocolState → AlpsRole"
          <| fun () ->
              let ext =
                  { Id = "protocolState"
                    Href = None
                    Value = Some "authenticated" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsRole("protocolState", "authenticated")))
                  "protocolState → AlpsRole"

          // Duality extensions
          testCase "clientObligation → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "clientObligation"
                    Href = None
                    Value = Some "must-ack" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality("clientObligation", "must-ack")))
                  "clientObligation → AlpsDuality"

          testCase "advancesProtocol → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "advancesProtocol"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsDuality("advancesProtocol", "true")))
                  "advancesProtocol → AlpsDuality"

          testCase "dualOf → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "dualOf"
                    Href = None
                    Value = Some "serverAction" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsDuality("dualOf", "serverAction"))) "dualOf → AlpsDuality"

          testCase "cutPoint → AlpsDuality"
          <| fun () ->
              let ext =
                  { Id = "cutPoint"
                    Href = None
                    Value = Some "true" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsDuality("cutPoint", "true"))) "cutPoint → AlpsDuality"

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
                  (AlpsAnnotation(AlpsAvailableInStates [ "XTurn"; "OTurn" ]))
                  "availableInStates → AlpsAvailableInStates"

          testCase "availableInStates single state"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "Won" }

              let result = classifyExtension ext
              Expect.equal result (AlpsAnnotation(AlpsAvailableInStates [ "Won" ])) "single state"

          testCase "availableInStates trims whitespace around entries"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "XTurn, OTurn , Won" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsAvailableInStates [ "XTurn"; "OTurn"; "Won" ]))
                  "whitespace trimmed"

          testCase "availableInStates ignores empty entries from double commas"
          <| fun () ->
              let ext =
                  { Id = "availableInStates"
                    Href = None
                    Value = Some "XTurn,,OTurn" }

              let result = classifyExtension ext

              Expect.equal
                  result
                  (AlpsAnnotation(AlpsAvailableInStates [ "XTurn"; "OTurn" ]))
                  "empty entries removed"

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
                  "unknown → AlpsExtension" ]

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
                    ReturnType = None
                    DocFormat = None
                    DocValue = None
                    Children = []
                    Extensions =
                      [ { Id = "projectedRole"
                          Href = None
                          Value = Some "PlayerX" } ]
                    Links = [] }

              let annotations = buildStateAnnotations descriptor

              let hasRole =
                  annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsRole("projectedRole", "PlayerX")) -> true
                      | _ -> false)

              Expect.isTrue hasRole "should contain AlpsRole annotation"

          testCase "state with unknown extension gets AlpsExtension annotation"
          <| fun () ->
              let descriptor =
                  { Id = Some "home"
                    Type = Some "semantic"
                    Href = None
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
                    ReturnType = Some "#OTurn"
                    DocFormat = None
                    DocValue = None
                    Children = []
                    Extensions =
                      [ { Id = "guard"
                          Href = None
                          Value = Some "role=PlayerX" }
                        { Id = "clientObligation"
                          Href = None
                          Value = Some "must-ack" } ]
                    Links = [] }

              let originalChild =
                  { Id = Some "makeMove"
                    Type = Some "unsafe"
                    Href = None
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
                      | AlpsAnnotation(AlpsDuality("clientObligation", "must-ack")) -> true
                      | _ -> false)

              let hasGuard =
                  annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsGuardExt _) -> true
                      | _ -> false)

              Expect.isTrue hasDuality "should contain AlpsDuality annotation"
              Expect.isFalse hasGuard "guard should be excluded from transition annotations" ]
