module Frank.Alps.Tests.ReferenceTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "rt, href, hrefExternal"
        [ test "rt sets Rt to the target descriptor value, not a string" {
              let product = semantic "product"
              let d = safe "listProducts" |> rt product
              Expect.equal d.Rt.Value.Id "product" ""
          }

          test "href sets InheritsFrom to DescriptorRef.Local wrapping the target" {
              let shared = semantic "shared"
              let d = semantic "local" |> href shared

              match d.InheritsFrom with
              | Some(DescriptorRef.Local t) -> Expect.equal t.Id "shared" ""
              | _ -> failwith "expected Local"
          }

          test "hrefExternal sets InheritsFrom to DescriptorRef.External wrapping a parsed Uri" {
              let d = semantic "local" |> hrefExternal "https://example.org/other-profile#shared"

              match d.InheritsFrom with
              | Some(DescriptorRef.External u) ->
                  Expect.equal u (System.Uri "https://example.org/other-profile#shared") ""
              | _ -> failwith "expected External"
          }

          test "rt and href/hrefExternal are independent fields -- setting one doesn't clear the other" {
              let product = semantic "product"
              let shared = semantic "shared"
              let d = safe "listProducts" |> rt product |> href shared
              Expect.isTrue d.Rt.IsSome ""
              Expect.isTrue d.InheritsFrom.IsSome ""
          } ]
