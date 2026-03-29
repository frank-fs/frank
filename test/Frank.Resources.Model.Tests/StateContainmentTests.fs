module Frank.Resources.Model.Tests.StateContainmentTests

open Expecto
open FsCheck
open FsCheck.FSharp
open Frank.Resources.Model

// -- Tests --

[<Tests>]
let stateContainmentTests =
    testList
        "StateContainment"
        [ testCase "empty has no parents or children"
          <| fun _ ->
              let c = StateContainment.empty
              Expect.isTrue (StateContainment.isEmpty c) "Empty containment"
              Expect.isEmpty (StateContainment.children "x" c) "No children"
              Expect.isNone (StateContainment.parent "x" c) "No parent"

          testCase "ofPairs builds correct maps"
          <| fun _ ->
              let c = StateContainment.ofPairs [ ("Parent", [ "A"; "B"; "C" ]) ]

              Expect.equal (StateContainment.children "Parent" c) [ "A"; "B"; "C" ] "Parent's children"
              Expect.equal (StateContainment.parent "A" c) (Some "Parent") "A's parent"
              Expect.equal (StateContainment.parent "B" c) (Some "Parent") "B's parent"
              Expect.isNone (StateContainment.parent "Parent" c) "Parent has no parent"

          testCase "isComposite true for parents, false for leaves"
          <| fun _ ->
              let c = StateContainment.ofPairs [ ("P", [ "X"; "Y" ]) ]
              Expect.isTrue (StateContainment.isComposite "P" c) "P is composite"
              Expect.isFalse (StateContainment.isComposite "X" c) "X is atomic"
              Expect.isFalse (StateContainment.isComposite "Unknown" c) "Unknown is not composite"

          testCase "allDescendants returns all transitive children"
          <| fun _ ->
              let c =
                  StateContainment.ofPairs
                      [ ("Root", [ "A"; "B" ])
                        ("A", [ "A1"; "A2" ]) ]

              let rootDescs = StateContainment.allDescendants "Root" c |> Set.ofList
              Expect.contains rootDescs "A" "A is descendant of Root"
              Expect.contains rootDescs "B" "B is descendant of Root"
              Expect.contains rootDescs "A1" "A1 is descendant of Root (grandchild)"
              Expect.contains rootDescs "A2" "A2 is descendant of Root (grandchild)"
              Expect.equal rootDescs.Count 4 "4 descendants total"

          testCase "allDescendants of leaf returns empty"
          <| fun _ ->
              let c = StateContainment.ofPairs [ ("P", [ "X" ]) ]
              Expect.isEmpty (StateContainment.allDescendants "X" c) "Leaf has no descendants"
              Expect.isEmpty (StateContainment.allDescendants "Unknown" c) "Unknown has no descendants" ]

// -- FsCheck property tests --

/// Generator for a simple hierarchy: one parent with N children.
let private genSimpleHierarchy: Gen<string * string list> =
    gen {
        let! childCount = Gen.choose (1, 5)

        let! children =
            Gen.elements [ "A"; "B"; "C"; "D"; "E"; "F"; "G"; "H" ]
            |> Gen.listOfLength childCount
            |> Gen.map List.distinct

        let children = if children.IsEmpty then [ "A" ] else children
        return ("Parent", children)
    }

[<Tests>]
let stateContainmentPropertyTests =
    testList
        "StateContainment FsCheck properties"
        [ testCase "every child has its parent (property)"
          <| fun _ ->
              Prop.forAll
                  (Arb.fromGen genSimpleHierarchy)
                  (fun (parent, children) ->
                      let c = StateContainment.ofPairs [ (parent, children) ]

                      children
                      |> List.forall (fun child -> StateContainment.parent child c = Some parent))
              |> Check.QuickThrowOnFailure

          testCase "parent's children list matches original (property)"
          <| fun _ ->
              Prop.forAll
                  (Arb.fromGen genSimpleHierarchy)
                  (fun (parent, children) ->
                      let c = StateContainment.ofPairs [ (parent, children) ]
                      StateContainment.children parent c = children)
              |> Check.QuickThrowOnFailure

          testCase "allDescendants count equals children count for single-level (property)"
          <| fun _ ->
              Prop.forAll
                  (Arb.fromGen genSimpleHierarchy)
                  (fun (parent, children) ->
                      let c = StateContainment.ofPairs [ (parent, children) ]
                      let descs = StateContainment.allDescendants parent c
                      descs.Length = children.Length)
              |> Check.QuickThrowOnFailure ]
