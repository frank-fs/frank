module Frank.Alps.Tests.DescriptorBuilderTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "descriptor { }"
        [ test "an empty block defaults to Type = Semantic, everything else empty" {
              let d = descriptor "productId" { () }
              Expect.equal d.Id "productId" ""
              Expect.equal d.Type DescriptorType.Semantic ""
              Expect.equal d.Doc None ""
          }

          test "semantic/safe/unsafe/idempotent as custom operations set Type" {
              Expect.equal (descriptor "a" { semantic }).Type DescriptorType.Semantic ""
              Expect.equal (descriptor "a" { safe }).Type DescriptorType.Safe ""
              Expect.equal (descriptor "a" { unsafe }).Type DescriptorType.Unsafe ""
              Expect.equal (descriptor "a" { idempotent }).Type DescriptorType.Idempotent ""
          }

          test "doc/def/tag/rel/named compose in one block" {
              let d =
                  descriptor "productId" {
                      def "https://schema.org/productID"
                      doc "The product's id"
                      tag "core"
                      rel "self"
                      named "id"
                  }

              Expect.equal d.Def.Value (System.Uri "https://schema.org/productID") ""
              Expect.equal d.Doc.Value.Value "The product's id" ""
              Expect.equal d.Tag [ "core" ] ""
              Expect.equal d.Rel (Some "self") ""
              Expect.equal d.Name (Some "id") ""
          }

          test "contains, rt, from, initial, regions all work as custom operations" {
              let productId = descriptor "productId" { def "https://schema.org/productID" }
              let product = descriptor "product" { contains [ productId ] }
              let openState = descriptor "open" { () }
              let closedState = descriptor "closed" { () }

              let listProducts = descriptor "listProducts" { safe; rt product }

              let makeMove =
                  descriptor "makeMove" {
                      unsafe
                      from [ openState ]
                      rt closedState
                  }

              let waitingForPlayer = descriptor "waitingForPlayer" { initial }
              let inGame = descriptor "inGame" { regions [ openState; closedState ] }

              Expect.equal listProducts.Rt.Value.Id "product" ""
              Expect.equal makeMove.From.[0].Id "open" ""
              Expect.equal makeMove.Rt.Value.Id "closed" ""
              Expect.isTrue (waitingForPlayer.Ext |> List.exists (fun e -> e.Id = InitialExtId)) ""
              Expect.isTrue (inGame.Ext |> List.exists (fun e -> e.Id = OrthogonalExtId)) ""
          }

          test "href, hrefExternal, ext, extWith, link, linkWith, docWith all work as custom operations" {
              let shared = descriptor "shared" { () }

              let d =
                  descriptor "local" {
                      href shared
                      ext "x" "1"
                      link "https://example.org" "help"
                  }

              Expect.isTrue d.InheritsFrom.IsSome ""
              Expect.equal d.Ext.Length 1 ""
              Expect.equal d.Link.Length 1 ""

              let e = descriptor "external" { hrefExternal "https://example.org/other#thing" }
              Expect.isTrue e.InheritsFrom.IsSome ""
          }

          test "the same profile built via |> and via descriptor { } is structurally equal" {
              let viaPlain = semantic "productId" |> def "https://schema.org/productID" |> doc "The id"
              let viaCe = descriptor "productId" { def "https://schema.org/productID"; doc "The id" }
              Expect.equal viaPlain viaCe ""
          } ]
