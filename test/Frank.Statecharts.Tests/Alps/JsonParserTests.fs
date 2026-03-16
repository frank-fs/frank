module Frank.Statecharts.Tests.Alps.JsonParserTests

open Expecto
open Frank.Statecharts.Alps.Types
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Tests.Alps.GoldenFiles

/// Recursively collect all descriptors (flattened) from a descriptor tree.
let rec private collectDescriptors (descriptors: Descriptor list) : Descriptor list =
    descriptors
    |> List.collect (fun d -> d :: collectDescriptors d.Descriptors)

/// Find a descriptor by id in a flat list.
let private findById (id: string) (descriptors: Descriptor list) : Descriptor option =
    descriptors |> List.tryFind (fun d -> d.Id = Some id)

[<Tests>]
let jsonParserTests =
    testList
        "Alps.JsonParser"
        [ testCase "parse tic-tac-toe golden file succeeds"
          <| fun _ ->
              let result = parseAlpsJson ticTacToeAlpsJson
              let doc = Expect.wantOk result "should parse successfully"
              Expect.equal doc.Version (Some "1.0") "version"
              Expect.isSome doc.Documentation "should have documentation"

              let docValue = doc.Documentation.Value
              Expect.equal docValue.Value "Tic-Tac-Toe game state machine" "doc value"
              Expect.equal docValue.Format (Some "text") "doc format"

          testCase "tic-tac-toe has all top-level descriptors"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              // 8 top-level descriptors: gameState, position, player, XTurn, OTurn, Won, Draw, viewGame
              Expect.equal doc.Descriptors.Length 8 "8 top-level descriptors"

          testCase "tic-tac-toe has all state descriptors"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let topIds = doc.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList

              Expect.containsAll
                  topIds
                  (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ])
                  "all state descriptors present"

          testCase "tic-tac-toe has semantic data descriptors"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let topIds = doc.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList

              Expect.containsAll
                  topIds
                  (Set.ofList [ "gameState"; "position"; "player" ])
                  "all data descriptors present"

          testCase "tic-tac-toe makeMove has correct type and rt"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              // Find XTurn and get its first makeMove child
              let xTurn =
                  doc.Descriptors |> List.find (fun d -> d.Id = Some "XTurn")

              let makeMove =
                  xTurn.Descriptors |> List.find (fun d -> d.Id = Some "makeMove")

              Expect.equal makeMove.Type Unsafe "makeMove type is Unsafe"
              Expect.equal makeMove.ReturnType (Some "#OTurn") "first makeMove rt is #OTurn"

          testCase "tic-tac-toe XTurn has three makeMove descriptors with different targets"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let xTurn =
                  doc.Descriptors |> List.find (fun d -> d.Id = Some "XTurn")

              let makeMoves =
                  xTurn.Descriptors
                  |> List.filter (fun d -> d.Id = Some "makeMove")

              Expect.equal makeMoves.Length 3 "XTurn has 3 makeMove descriptors"

              let targets = makeMoves |> List.map (fun d -> d.ReturnType) |> Set.ofList

              Expect.containsAll
                  targets
                  (Set.ofList [ Some "#OTurn"; Some "#Won"; Some "#Draw" ])
                  "all targets present"

          testCase "tic-tac-toe guards are captured in ext elements"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let xTurn =
                  doc.Descriptors |> List.find (fun d -> d.Id = Some "XTurn")

              let makeMoves =
                  xTurn.Descriptors
                  |> List.filter (fun d -> d.Id = Some "makeMove")

              // First makeMove has guard "role=PlayerX"
              let firstMove = makeMoves.[0]
              Expect.equal firstMove.Extensions.Length 1 "first makeMove has 1 ext"
              Expect.equal firstMove.Extensions.[0].Id "guard" "ext id is guard"
              Expect.equal firstMove.Extensions.[0].Value (Some "role=PlayerX") "guard value"

              // Second makeMove has guard "wins"
              let secondMove = makeMoves.[1]
              Expect.equal secondMove.Extensions.[0].Value (Some "wins") "wins guard"

              // Third makeMove has guard "boardFull"
              let thirdMove = makeMoves.[2]
              Expect.equal thirdMove.Extensions.[0].Value (Some "boardFull") "boardFull guard"

          testCase "tic-tac-toe makeMove has nested parameter descriptors"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let xTurn =
                  doc.Descriptors |> List.find (fun d -> d.Id = Some "XTurn")

              let makeMove =
                  xTurn.Descriptors |> List.find (fun d -> d.Id = Some "makeMove")

              // makeMove has 2 child descriptors: href to #position and #player
              Expect.equal makeMove.Descriptors.Length 2 "makeMove has 2 nested descriptors"
              Expect.equal makeMove.Descriptors.[0].Href (Some "#position") "first param href"
              Expect.equal makeMove.Descriptors.[1].Href (Some "#player") "second param href"

          testCase "tic-tac-toe viewGame is safe type with rt"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let viewGame =
                  doc.Descriptors |> List.find (fun d -> d.Id = Some "viewGame")

              Expect.equal viewGame.Type Safe "viewGame type is Safe"
              Expect.equal viewGame.ReturnType (Some "#gameState") "viewGame rt"

          testCase "tic-tac-toe has link element"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Links.Length 1 "one link"
              Expect.equal doc.Links.[0].Rel "self" "link rel"
              Expect.equal doc.Links.[0].Href "http://example.com/alps/tic-tac-toe" "link href"

          testCase "tic-tac-toe XTurn has href-only viewGame reference"
          <| fun _ ->
              let doc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let xTurn =
                  doc.Descriptors |> List.find (fun d -> d.Id = Some "XTurn")

              let hrefOnly =
                  xTurn.Descriptors
                  |> List.find (fun d -> d.Id = None && d.Href = Some "#viewGame")

              Expect.isNone hrefOnly.Id "href-only descriptor has no id"
              Expect.equal hrefOnly.Href (Some "#viewGame") "href-only descriptor href"

          testCase "parse onboarding golden file succeeds"
          <| fun _ ->
              let result = parseAlpsJson onboardingAlpsJson
              let doc = Expect.wantOk result "should parse successfully"
              Expect.equal doc.Version (Some "1.0") "version"
              Expect.isSome doc.Documentation "should have documentation"

          testCase "onboarding has all state descriptors"
          <| fun _ ->
              let doc =
                  parseAlpsJson onboardingAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let topIds = doc.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList

              Expect.containsAll
                  topIds
                  (Set.ofList [ "Welcome"; "CollectEmail"; "CollectProfile"; "Review"; "Complete" ])
                  "all onboarding states present"

          testCase "onboarding transitions have correct types"
          <| fun _ ->
              let doc =
                  parseAlpsJson onboardingAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let allDescs = collectDescriptors doc.Descriptors

              // start is safe
              let start = findById "start" allDescs
              Expect.isSome start "start found"
              Expect.equal start.Value.Type Safe "start is safe"

              // submitEmail is unsafe
              let submitEmail = findById "submitEmail" allDescs
              Expect.isSome submitEmail "submitEmail found"
              Expect.equal submitEmail.Value.Type Unsafe "submitEmail is unsafe"

              // editEmail is safe
              let editEmail = findById "editEmail" allDescs
              Expect.isSome editEmail "editEmail found"
              Expect.equal editEmail.Value.Type Safe "editEmail is safe"

          testCase "onboarding submitEmail has parameter reference"
          <| fun _ ->
              let doc =
                  parseAlpsJson onboardingAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let allDescs = collectDescriptors doc.Descriptors
              let submitEmail = (findById "submitEmail" allDescs).Value
              Expect.equal submitEmail.Descriptors.Length 1 "one param"
              Expect.equal submitEmail.Descriptors.[0].Href (Some "#email") "param href"

          testCase "onboarding has no links"
          <| fun _ ->
              let doc =
                  parseAlpsJson onboardingAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.isEmpty doc.Links "no links in onboarding" ]

[<Tests>]
let jsonParserEdgeCaseTests =
    testList
        "Alps.JsonParser edge cases"
        [ testCase "empty ALPS document (no descriptors)"
          <| fun _ ->
              let json = """{"alps":{"descriptor":[]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.isEmpty doc.Descriptors "empty descriptors"

          testCase "ALPS document with no descriptor property"
          <| fun _ ->
              let json = """{"alps":{}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.isEmpty doc.Descriptors "no descriptors when property absent"

          testCase "descriptor without type defaults to Semantic"
          <| fun _ ->
              let json = """{"alps":{"descriptor":[{"id":"test"}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Descriptors.[0].Type Semantic "default to Semantic"

          testCase "unknown JSON properties are ignored"
          <| fun _ ->
              let json =
                  """{"alps":{"unknownProp":"value","descriptor":[{"id":"test","futureField":42}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Descriptors.Length 1 "descriptor parsed despite unknown props"

          testCase "ALPS document with only links (no descriptors)"
          <| fun _ ->
              let json =
                  """{"alps":{"link":[{"rel":"self","href":"http://example.com"}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.isEmpty doc.Descriptors "no descriptors"
              Expect.equal doc.Links.Length 1 "one link"

          testCase "descriptor with href to external URL"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"href":"http://example.com/profile"}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Descriptors.[0].Href (Some "http://example.com/profile") "external href"

          testCase "multiple ext elements on a single descriptor"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"id":"test","ext":[{"id":"guard","value":"role=X"},{"id":"meta","value":"info"}]}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Descriptors.[0].Extensions.Length 2 "two extensions"
              Expect.equal doc.Descriptors.[0].Extensions.[0].Id "guard" "first ext id"
              Expect.equal doc.Descriptors.[0].Extensions.[1].Id "meta" "second ext id"

          testCase "unicode characters in descriptor ids and doc values"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"id":"beschreibung","doc":{"value":"Beschreibung auf Deutsch"}}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Descriptors.[0].Id (Some "beschreibung") "unicode id"

              Expect.equal
                  doc.Descriptors.[0].Documentation.Value.Value
                  "Beschreibung auf Deutsch"
                  "unicode doc value"

          testCase "descriptor with no id (href-only reference)"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"href":"#otherDescriptor"}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.isNone doc.Descriptors.[0].Id "no id on href-only descriptor"
              Expect.equal doc.Descriptors.[0].Href (Some "#otherDescriptor") "href present"

          testCase "ALPS document with version only"
          <| fun _ ->
              let json = """{"alps":{"version":"1.0"}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Version (Some "1.0") "version captured"
              Expect.isEmpty doc.Descriptors "no descriptors"

          testCase "ALPS document with top-level ext elements"
          <| fun _ ->
              let json =
                  """{"alps":{"ext":[{"id":"custom","value":"data"}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Extensions.Length 1 "one top-level ext"
              Expect.equal doc.Extensions.[0].Id "custom" "ext id"
              Expect.equal doc.Extensions.[0].Value (Some "data") "ext value"

          testCase "ext element with href and no value"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"id":"test","ext":[{"id":"ref","href":"http://example.com/ext"}]}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let ext = doc.Descriptors.[0].Extensions.[0]
              Expect.equal ext.Href (Some "http://example.com/ext") "ext href"
              Expect.isNone ext.Value "ext value is None"

          testCase "deeply nested descriptors"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"id":"level1","descriptor":[{"id":"level2","descriptor":[{"id":"level3"}]}]}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Descriptors.[0].Id (Some "level1") "level1"
              Expect.equal doc.Descriptors.[0].Descriptors.[0].Id (Some "level2") "level2"

              Expect.equal
                  doc.Descriptors.[0].Descriptors.[0].Descriptors.[0].Id
                  (Some "level3")
                  "level3"

          testCase "descriptor with link elements"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"id":"test","link":[{"rel":"help","href":"http://example.com/help"}]}]}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.equal doc.Descriptors.[0].Links.Length 1 "one link on descriptor"
              Expect.equal doc.Descriptors.[0].Links.[0].Rel "help" "link rel"

          testCase "doc element with no format"
          <| fun _ ->
              let json =
                  """{"alps":{"doc":{"value":"Just some text"}}}"""

              let doc =
                  parseAlpsJson json
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              Expect.isSome doc.Documentation "doc present"
              Expect.isNone doc.Documentation.Value.Format "no format"
              Expect.equal doc.Documentation.Value.Value "Just some text" "doc text" ]

[<Tests>]
let jsonParserErrorTests =
    testList
        "Alps.JsonParser errors"
        [ testCase "malformed JSON returns error"
          <| fun _ ->
              let result = parseAlpsJson "not valid json"
              Expect.isError result "should be error"

          testCase "empty string returns error"
          <| fun _ ->
              let result = parseAlpsJson ""
              Expect.isError result "should be error"

          testCase "valid JSON but missing alps root returns error"
          <| fun _ ->
              let result = parseAlpsJson """{"descriptors":[]}"""
              Expect.isError result "should be error for missing alps root"

              match result with
              | Error errors -> Expect.isNonEmpty errors "should have error details"
              | Ok _ -> failwith "expected error"

          testCase "error description is actionable"
          <| fun _ ->
              let result = parseAlpsJson "not valid json"

              match result with
              | Error errors ->
                  Expect.isNonEmpty errors "should have errors"
                  Expect.isNotEmpty errors.[0].Description "error description not empty"
              | Ok _ -> failwith "expected error"

          testCase "JSON parse error has no position"
          <| fun _ ->
              let result = parseAlpsJson "not valid json"

              match result with
              | Error errors -> Expect.isNone errors.[0].Position "JSON errors have no position"
              | Ok _ -> failwith "expected error"

          testCase "missing alps root error has descriptive message"
          <| fun _ ->
              let result = parseAlpsJson """{"foo":"bar"}"""

              match result with
              | Error errors ->
                  Expect.stringContains errors.[0].Description "alps" "mentions alps"
              | Ok _ -> failwith "expected error"

          testCase "null JSON value returns error"
          <| fun _ ->
              let result = parseAlpsJson "null"
              // null is valid JSON but not a valid ALPS document
              Expect.isError result "should be error for null"

          testCase "JSON array returns error"
          <| fun _ ->
              let result = parseAlpsJson "[]"
              // An array is not a valid ALPS document (needs object with alps property)
              Expect.isError result "should be error for array" ]
