module Frank.Alps.Tests.CompositeStateTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "initial, regions, StateComposition"
        [ test "initial appends the canonical ext marker" {
              let d = semantic "waitingForPlayer" |> initial

              Expect.contains
                  d.Ext
                  { Id = "https://frank-fs.github.io/alps-ext/initial"
                    Href = None
                    Value = None
                    Tag = [] }
                  ""
          }

          test "contains raises when more than one direct child is marked initial" {
              let a = semantic "a" |> initial
              let b = semantic "b" |> initial

              Expect.throws (fun () -> semantic "parent" |> contains [ a; b ] |> ignore) ""
          }

          test "contains does not raise with zero or one initial child" {
              let a = semantic "a" |> initial
              let b = semantic "b"
              semantic "parent" |> contains [ a; b ] |> ignore
              semantic "parent" |> contains [ b ] |> ignore
          }

          test "regions sets Descriptors like contains, plus the orthogonal ext marker on the parent" {
              let network = semantic "network"
              let session = semantic "session"
              let d = semantic "inGame" |> regions [ network; session ]

              Expect.equal (d.Descriptors |> List.map (fun x -> x.Id)) [ "network"; "session" ] ""

              Expect.contains
                  d.Ext
                  { Id = "https://frank-fs.github.io/alps-ext/orthogonal"
                    Href = None
                    Value = None
                    Tag = [] }
                  ""
          }

          test "regions does not enforce the at-most-one-initial rule" {
              let a = semantic "a" |> initial
              let b = semantic "b" |> initial
              semantic "parent" |> regions [ a; b ] |> ignore
          }

          test "StateComposition.ofDescriptor: a descriptor with no Descriptors is Leaf" {
              Expect.equal (StateComposition.ofDescriptor (semantic "x")) StateComposition.Leaf ""
          }

          test "StateComposition.ofDescriptor: contains without the orthogonal marker is Alternatives" {
              let d = semantic "open" |> contains [ semantic "a"; semantic "b" ]

              match StateComposition.ofDescriptor d with
              | StateComposition.Alternatives children -> Expect.equal children.Length 2 ""
              | other -> failwithf "expected Alternatives, got %A" other
          }

          test "StateComposition.ofDescriptor: regions is Regions" {
              let d = semantic "inGame" |> regions [ semantic "network"; semantic "session" ]

              match StateComposition.ofDescriptor d with
              | StateComposition.Regions children -> Expect.equal children.Length 2 ""
              | other -> failwithf "expected Regions, got %A" other
          }

          test "StateComposition.initialChild finds the marked child among Alternatives" {
              let waiting = semantic "waitingForPlayer" |> initial
              let inProgress = semantic "inProgress"
              let d = semantic "open" |> contains [ waiting; inProgress ]

              Expect.equal (StateComposition.initialChild d) (Some waiting) ""
          }

          test "StateComposition.initialChild is None when no child is marked" {
              let d = semantic "open" |> contains [ semantic "a"; semantic "b" ]
              Expect.equal (StateComposition.initialChild d) None ""
          } ]
