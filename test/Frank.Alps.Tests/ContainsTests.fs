module Frank.Alps.Tests.ContainsTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "contains"
        [ test "contains sets Descriptors to the given list, in order" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let d = semantic "parent" |> contains [ a; b; c ]
              Expect.equal (d.Descriptors |> List.map (fun x -> x.Id)) [ "a"; "b"; "c" ] ""
          }

          test "contains accepts children of any DescriptorType, not just semantic" {
              // draft-07 §2.2.4: any descriptor type may nest under any other. This is what leaves
              // room for composite/substate hierarchy later -- contains is untyped by design.
              let child = safe "listChildren"
              let d = semantic "parent" |> contains [ child ]
              Expect.equal d.Descriptors.[0].Type DescriptorType.Safe ""
          }

          test "contains called on an already-contains'd descriptor replaces Descriptors, not appends" {
              // Unlike tag/ext/link (append-only, multiple calls compose), contains sets the whole
              // nested-descriptor list at once -- there is exactly one `descriptor` array per parent
              // in the wire format, so a second call is a deliberate replacement, not an accumulation.
              let a, b = semantic "a", semantic "b"
              let d = semantic "parent" |> contains [ a ] |> contains [ b ]
              Expect.equal (d.Descriptors |> List.map (fun x -> x.Id)) [ "b" ] ""
          }

          test "nesting is recursive: a contains'd child can itself contain further children" {
              let grandchild = semantic "grandchild"
              let child = semantic "child" |> contains [ grandchild ]
              let d = semantic "parent" |> contains [ child ]
              Expect.equal d.Descriptors.[0].Descriptors.[0].Id "grandchild" ""
          } ]
