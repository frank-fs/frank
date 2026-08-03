module Frank.Alps.Tests.DescriptorTreeTests

open Expecto
open Frank.Alps

let private ids (ds: Descriptor list) = ds |> List.map (fun d -> d.Id)

[<Tests>]
let tests =
    testList
        "DescriptorTree"
        [ test "flatten yields the descriptor itself first, then each child's subtree in authoring order" {
              let a = semantic "a"
              let b = semantic "b" |> contains [ semantic "b1"; semantic "b2" ]
              let root = semantic "root" |> contains [ a; b ]

              Expect.equal (ids (DescriptorTree.flatten root)) [ "root"; "a"; "b"; "b1"; "b2" ] "Depth-first, authoring order"
          }

          test "flatten of a leaf is just the leaf" {
              Expect.equal (ids (DescriptorTree.flatten (semantic "leaf"))) [ "leaf" ] ""
          }

          test "flattenAll covers every root" {
              let profile = [ semantic "x" |> contains [ safe "x1" ]; semantic "y" ]
              Expect.equal (ids (DescriptorTree.flattenAll profile)) [ "x"; "x1"; "y" ] ""
          }

          test "prune keeps a Semantic descriptor even when its id is not allowed" {
              let profile = [ semantic "vocabulary" ]
              Expect.equal (ids (DescriptorTree.prune Set.empty profile)) [ "vocabulary" ] "Vocabulary is never filtered"
          }

          test "prune drops a top-level transition whose id is not allowed" {
              let profile = [ safe "listProducts"; unsafe "createProduct" ]

              Expect.equal
                  (ids (DescriptorTree.prune (Set.ofList [ "listProducts" ]) profile))
                  [ "listProducts" ]
                  "Only the allowed transition survives"
          }

          test "prune recurses into a kept Semantic parent and drops a disallowed nested transition" {
              // The exact shape that bypassed filtering entirely before: the transition is never a
              // top-level entry, and the Semantic parent is always kept.
              let profile = [ semantic "game" |> contains [ safe "viewGame"; unsafe "makeMove" ] ]

              let pruned = DescriptorTree.prune (Set.ofList [ "viewGame" ]) profile

              Expect.equal (ids pruned) [ "game" ] "The parent survives"
              Expect.equal (ids pruned.Head.Descriptors) [ "viewGame" ] "The disallowed nested transition is gone"
          }

          test "prune recurses into a kept transition's own children" {
              // Nothing in the type system stops `contains` from being applied to a transition, and
              // Serialization.writeDescriptor recurses into Descriptors unconditionally.
              let profile =
                  [ safe "viewGame" |> contains [ semantic "board"; unsafe "resign" ] ]

              let pruned = DescriptorTree.prune (Set.ofList [ "viewGame" ]) profile

              Expect.equal (ids pruned) [ "viewGame" ] ""
              Expect.equal (ids pruned.Head.Descriptors) [ "board" ] "Semantic child kept, disallowed nested transition dropped"
          }

          test "prune reaches arbitrarily deep nesting" {
              let profile =
                  [ semantic "top" |> contains [ semantic "mid" |> contains [ unsafe "deepMove" ] ] ]

              let denied = DescriptorTree.prune Set.empty profile
              let allowed = DescriptorTree.prune (Set.ofList [ "deepMove" ]) profile

              Expect.equal (ids denied.Head.Descriptors.Head.Descriptors) [] "Three levels down, still filtered"
              Expect.equal (ids allowed.Head.Descriptors.Head.Descriptors) [ "deepMove" ] "…and still served when allowed"
          } ]
