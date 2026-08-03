module Frank.Alps.Tests.ConstructorTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "Constructors and simple combinators"
        [ test "semantic sets Id and Type, everything else empty" {
              let d = semantic "product"
              Expect.equal d.Id "product" ""
              Expect.equal d.Type DescriptorType.Semantic ""
              Expect.equal d.Doc None ""
              Expect.equal d.Descriptors [] ""
          }

          test "safe/unsafe/idempotent set the expected Type" {
              Expect.equal (safe "listProducts").Type DescriptorType.Safe ""
              Expect.equal (unsafe "createProduct").Type DescriptorType.Unsafe ""
              Expect.equal (idempotent "replaceProduct").Type DescriptorType.Idempotent ""
          }

          test "doc sets a shorthand Doc with only Value populated" {
              let d = semantic "price" |> doc "Price in minor units"
              Expect.equal d.Doc.Value.Value "Price in minor units" ""
              Expect.equal d.Doc.Value.Href None ""
              Expect.equal d.Doc.Value.Format None ""
          }

          test "docWith sets the full Doc record verbatim" {
              let full =
                  { Value = "Price"
                    Href = Some(System.Uri "https://example.org/docs/price")
                    Format = Some DocFormat.Markdown
                    ContentType = Some "text/markdown"
                    Tag = [ "money" ] }

              let d = semantic "price" |> docWith full
              Expect.equal d.Doc.Value full ""
          }

          test "def sets Def as a parsed Uri" {
              let d = semantic "productId" |> def "https://schema.org/productID"
              Expect.equal d.Def.Value (System.Uri "https://schema.org/productID") ""
          }

          test "tag sets Tag" {
              let d = semantic "price" |> tag "money currency"
              Expect.equal d.Tag [ "money currency" ] ""
          }

          test "tag called twice appends, not replaces" {
              let d = semantic "price" |> tag "money" |> tag "currency"
              Expect.equal d.Tag [ "money"; "currency" ] ""
          }

          test "rel sets Rel" {
              let d = semantic "product" |> rel "tag:example.com,2026:product"
              Expect.equal d.Rel (Some "tag:example.com,2026:product") ""
          }

          test "named sets Name" {
              let d = semantic "productId" |> named "id"
              Expect.equal d.Name (Some "id") ""
          } ]
