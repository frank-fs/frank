module Frank.Alps.Tests.ExtLinkTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "ext and link"
        [ test "ext appends an Ext with Id and Value set, Href/Tag empty" {
              let d = semantic "state" |> ext "https://frank-fs.github.io/alps-ext/example" "value"

              Expect.equal
                  d.Ext
                  [ { Id = "https://frank-fs.github.io/alps-ext/example"
                      Href = None
                      Value = Some "value"
                      Tag = [] } ]
                  ""
          }

          test "ext called twice appends both, order preserved" {
              let d = semantic "state" |> ext "a" "1" |> ext "b" "2"
              Expect.equal (d.Ext |> List.map (fun e -> e.Id)) [ "a"; "b" ] ""
          }

          test "extWith appends a full Ext record verbatim" {
              let full =
                  { Id = "https://frank-fs.github.io/alps-ext/example"
                    Href = Some(System.Uri "https://frank-fs.github.io/alps-ext/")
                    Value = Some "value"
                    Tag = [ "internal" ] }

              let d = semantic "state" |> extWith full
              Expect.equal d.Ext [ full ] ""
          }

          test "link appends a Link with Href and Rel set, Title/Tag empty" {
              let d = semantic "product" |> link "https://example.org/docs" "help"

              Expect.equal
                  d.Link
                  [ { Href = System.Uri "https://example.org/docs"
                      Rel = "help"
                      Title = None
                      Tag = [] } ]
                  ""
          }

          test "linkWith appends a full Link record verbatim" {
              let full =
                  { Href = System.Uri "https://example.org/docs"
                    Rel = "tag-doc"
                    Title = Some "Tag vocabulary"
                    Tag = [] }

              let d = semantic "product" |> linkWith full
              Expect.equal d.Link [ full ] ""
          }

          test "link called twice appends both" {
              let d = semantic "product" |> link "https://a" "help" |> link "https://b" "tag-doc"
              Expect.equal d.Link.Length 2 ""
          } ]
