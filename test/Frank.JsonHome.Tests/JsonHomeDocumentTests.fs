module Frank.JsonHome.Tests.JsonHomeDocumentTests

open System.Text.Json
open Expecto
open Frank.JsonHome

let private parse (json: string) = JsonDocument.Parse(json).RootElement

let private widgets =
    { Rel = "tag:me@example.com,2016:widgets"
      Href = "/widgets/"
      IsTemplated = false
      HrefVars = []
      Methods = [ "GET" ]
      Formats = []
      Accepts = []
      Docs = None
      Status = None
      Metadata = [] }

let private widget =
    { Rel = "tag:me@example.com,2016:widget"
      Href = "/widgets/{widget_id}"
      IsTemplated = true
      HrefVars = [ "widget_id", "https://example.org/param/widget" ]
      Methods = [ "GET"; "PUT"; "DELETE"; "PATCH" ]
      Formats = [ "application/json" ]
      Accepts = [ "PATCH", [ "application/json-patch+json" ] ]
      Docs = None
      Status = None
      Metadata = [] }

[<Tests>]
let tests =
    testList
        "JsonHome.serialize"
        [ test "reproduces the draft-06 example document" {
              let options =
                  { JsonHomeOptions.Default with
                      Title = Some "Example API"
                      Links = [ "author", "mailto:api-admin@example.com" ] }

              let root = parse (JsonHome.serialize options [ widgets; widget ])

              Expect.equal (root.GetProperty("api").GetProperty("title").GetString()) "Example API" "api.title"

              Expect.equal
                  (root.GetProperty("api").GetProperty("links").GetProperty("author").GetString())
                  "mailto:api-admin@example.com"
                  "api.links.author"

              let resources = root.GetProperty "resources"

              let widgetsEntry = resources.GetProperty "tag:me@example.com,2016:widgets"
              Expect.equal (widgetsEntry.GetProperty("href").GetString()) "/widgets/" "href for the collection"

              let widgetEntry = resources.GetProperty "tag:me@example.com,2016:widget"

              Expect.equal
                  (widgetEntry.GetProperty("hrefTemplate").GetString())
                  "/widgets/{widget_id}"
                  "hrefTemplate for the item"

              Expect.isFalse (fst (widgetEntry.TryGetProperty "href")) "Templated resources omit href"

              Expect.equal
                  (widgetEntry.GetProperty("hrefVars").GetProperty("widget_id").GetString())
                  "https://example.org/param/widget"
                  "hrefVars"

              let hints = widgetEntry.GetProperty "hints"

              Expect.equal
                  (hints.GetProperty("allow").EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq)
                  [ "GET"; "PUT"; "DELETE"; "PATCH" ]
                  "allow"

              Expect.equal
                  (hints.GetProperty("formats").GetProperty("application/json").ValueKind)
                  JsonValueKind.Object
                  "formats is an object of empty objects"

              Expect.equal
                  (hints.GetProperty("acceptPatch").EnumerateArray()
                   |> Seq.map (fun e -> e.GetString())
                   |> List.ofSeq)
                  [ "application/json-patch+json" ]
                  "acceptPatch uses the camelCase draft-06 name"
          }

          test "omits api when unconfigured and omits empty hints" {
              let root = parse (JsonHome.serialize JsonHomeOptions.Default [ widgets ])

              Expect.isFalse (fst (root.TryGetProperty "api")) "No api member when nothing is configured"

              let entry = root.GetProperty("resources").GetProperty "tag:me@example.com,2016:widgets"
              let hints = entry.GetProperty "hints"
              Expect.isFalse (fst (hints.TryGetProperty "formats")) "No formats hint when none are declared"
          }

          test "emits the status hint" {
              let root =
                  parse (
                      JsonHome.serialize JsonHomeOptions.Default [ { widgets with Status = Some ResourceStatus.Gone } ]
                  )

              let hints =
                  root.GetProperty("resources").GetProperty("tag:me@example.com,2016:widgets").GetProperty "hints"

              Expect.equal (hints.GetProperty("status").GetString()) "gone" "status"
          } ]
