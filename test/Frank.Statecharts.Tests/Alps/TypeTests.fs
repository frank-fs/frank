module Frank.Statecharts.Tests.Alps.TypeTests

open Expecto
open Frank.Statecharts.Alps.Types

[<Tests>]
let typeTests =
    testList
        "Alps.Types"
        [
          // === Construction tests ===
          testCase "empty AlpsDocument can be constructed"
          <| fun _ ->
              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              Expect.isNone doc.Version "version is None"
              Expect.isNone doc.Documentation "documentation is None"
              Expect.isEmpty doc.Descriptors "no descriptors"
              Expect.isEmpty doc.Links "no links"
              Expect.isEmpty doc.Extensions "no extensions"

          testCase "Descriptor with all fields populated"
          <| fun _ ->
              let desc =
                  { Id = Some "testDescriptor"
                    Type = Unsafe
                    Href = Some "#target"
                    ReturnType = Some "#returnState"
                    Documentation = Some { Format = Some "text"; Value = "A test descriptor" }
                    Descriptors =
                        [ { Id = Some "child"
                            Type = Semantic
                            Href = None
                            ReturnType = None
                            Documentation = None
                            Descriptors = []
                            Extensions = []
                            Links = [] } ]
                    Extensions = [ { Id = "guard"; Href = None; Value = Some "role=admin" } ]
                    Links = [ { Rel = "help"; Href = "http://example.com/help" } ] }

              Expect.equal desc.Id (Some "testDescriptor") "id matches"
              Expect.equal desc.Type Unsafe "type is Unsafe"
              Expect.equal desc.Href (Some "#target") "href matches"
              Expect.equal desc.ReturnType (Some "#returnState") "rt matches"
              Expect.isSome desc.Documentation "documentation is present"
              Expect.equal (List.length desc.Descriptors) 1 "one nested descriptor"
              Expect.equal (List.length desc.Extensions) 1 "one extension"
              Expect.equal (List.length desc.Links) 1 "one link"

          testCase "Descriptor with minimal fields (id and default type)"
          <| fun _ ->
              let desc =
                  { Id = Some "minimal"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              Expect.equal desc.Id (Some "minimal") "id matches"
              Expect.equal desc.Type Semantic "type defaults to Semantic"
              Expect.isNone desc.Href "no href"
              Expect.isNone desc.ReturnType "no rt"
              Expect.isNone desc.Documentation "no documentation"
              Expect.isEmpty desc.Descriptors "no nested descriptors"
              Expect.isEmpty desc.Extensions "no extensions"
              Expect.isEmpty desc.Links "no links"

          testCase "nested Descriptors preserve hierarchy"
          <| fun _ ->
              let grandchild =
                  { Id = Some "grandchild"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let child =
                  { Id = Some "child"
                    Type = Safe
                    Href = None
                    ReturnType = Some "#grandchild"
                    Documentation = None
                    Descriptors = [ grandchild ]
                    Extensions = []
                    Links = [] }

              let parent =
                  { Id = Some "parent"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = [ child ]
                    Extensions = []
                    Links = [] }

              Expect.equal (List.length parent.Descriptors) 1 "parent has one child"

              let retrievedChild = parent.Descriptors.[0]
              Expect.equal retrievedChild.Id (Some "child") "child id matches"
              Expect.equal (List.length retrievedChild.Descriptors) 1 "child has one grandchild"

              let retrievedGrandchild = retrievedChild.Descriptors.[0]
              Expect.equal retrievedGrandchild.Id (Some "grandchild") "grandchild id matches"
              Expect.isEmpty retrievedGrandchild.Descriptors "grandchild has no children"

          // === Structural equality tests ===
          testCase "two identical AlpsDocuments are equal"
          <| fun _ ->
              let desc =
                  { Id = Some "state1"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = Some { Format = Some "text"; Value = "A state" }
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let doc1 =
                  { Version = Some "1.0"
                    Documentation = Some { Format = Some "text"; Value = "Test doc" }
                    Descriptors = [ desc ]
                    Links = [ { Rel = "self"; Href = "http://example.com" } ]
                    Extensions = [] }

              let doc2 =
                  { Version = Some "1.0"
                    Documentation = Some { Format = Some "text"; Value = "Test doc" }
                    Descriptors = [ desc ]
                    Links = [ { Rel = "self"; Href = "http://example.com" } ]
                    Extensions = [] }

              Expect.equal doc1 doc2 "identical documents are equal"

          testCase "two different AlpsDocuments are not equal"
          <| fun _ ->
              let doc1 =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let doc2 =
                  { Version = Some "2.0"
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              Expect.notEqual doc1 doc2 "documents with different versions are not equal"

          testCase "Descriptor equality includes nested children"
          <| fun _ ->
              let childA =
                  { Id = Some "child"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let childB =
                  { Id = Some "differentChild"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let descA =
                  { Id = Some "parent"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = [ childA ]
                    Extensions = []
                    Links = [] }

              let descB =
                  { Id = Some "parent"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = [ childB ]
                    Extensions = []
                    Links = [] }

              Expect.notEqual descA descB "descriptors with different children are not equal"

          // === DescriptorType tests ===
          testCase "all four DescriptorType cases exist"
          <| fun _ ->
              let cases = [ Semantic; Safe; Unsafe; Idempotent ]
              Expect.equal (List.length cases) 4 "four descriptor type cases"
              Expect.notEqual Semantic Safe "Semantic <> Safe"
              Expect.notEqual Safe Unsafe "Safe <> Unsafe"
              Expect.notEqual Unsafe Idempotent "Unsafe <> Idempotent"
              Expect.notEqual Idempotent Semantic "Idempotent <> Semantic"

          // === AlpsParseError tests ===
          testCase "AlpsParseError with position"
          <| fun _ ->
              let error =
                  { Description = "Missing id attribute"
                    Position = Some { Line = 5; Column = 12 } }

              Expect.equal error.Description "Missing id attribute" "description matches"
              Expect.isSome error.Position "position is present"

              let pos = error.Position.Value
              Expect.equal pos.Line 5 "line matches"
              Expect.equal pos.Column 12 "column matches"

          testCase "AlpsParseError without position"
          <| fun _ ->
              let error =
                  { Description = "Invalid JSON structure"
                    Position = None }

              Expect.equal error.Description "Invalid JSON structure" "description matches"
              Expect.isNone error.Position "position is None"
        ]
