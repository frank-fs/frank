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
      AcceptRanges = []
      AcceptPrefer = []
      PreconditionRequired = []
      AuthSchemes = []
      Docs = None
      Status = None
      Metadata = []
      MethodMetadata = [ "GET", [] ] }

let private widget =
    { Rel = "tag:me@example.com,2016:widget"
      Href = "/widgets/{widget_id}"
      IsTemplated = true
      HrefVars = [ "widget_id", "https://example.org/param/widget" ]
      Methods = [ "GET"; "PUT"; "DELETE"; "PATCH" ]
      Formats = [ "application/json" ]
      Accepts = [ "PATCH", [ "application/json-patch+json" ] ]
      AcceptRanges = []
      AcceptPrefer = []
      PreconditionRequired = []
      AuthSchemes = []
      Docs = None
      Status = None
      Metadata = []
      MethodMetadata = [ "GET", []; "PUT", []; "DELETE", []; "PATCH", [] ] }

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

          test "templated resources always emit hrefVars, even when none are declared" {
              let root = parse (JsonHome.serialize JsonHomeOptions.Default [ { widget with HrefVars = [] } ])

              let entry = root.GetProperty("resources").GetProperty "tag:me@example.com,2016:widget"

              // draft-06 section 4: "When hrefTemplate is present, the Resource
              // Object MUST have a hrefVars property."
              let found, hrefVars = entry.TryGetProperty "hrefVars"
              Expect.isTrue found "hrefVars accompanies every hrefTemplate"
              Expect.equal hrefVars.ValueKind JsonValueKind.Object "hrefVars is an object"
              Expect.isEmpty (hrefVars.EnumerateObject() |> List.ofSeq) "Empty when nothing was declared"
          }

          test "resources with no hints omit the hints member" {
              let bare =
                  { widgets with
                      Methods = []
                      Formats = []
                      Accepts = []
                      Docs = None
                      Status = None }

              let root = parse (JsonHome.serialize JsonHomeOptions.Default [ bare ])
              let entry = root.GetProperty("resources").GetProperty "tag:me@example.com,2016:widgets"

              Expect.isFalse (fst (entry.TryGetProperty "hints")) "No empty hints object"
          }

          test "emits acceptPost and acceptPut, and no accept hint for other methods" {
              let widget =
                  { widgets with
                      Accepts =
                          [ "POST", [ "application/json" ]
                            "PUT", [ "application/json"; "application/merge-patch+json" ]
                            // GET is not one of the three methods draft-06 names an
                            // accept hint for, so its entry contributes nothing.
                            "GET", [ "application/x-should-not-appear" ] ] }

              let root = parse (JsonHome.serialize JsonHomeOptions.Default [ widget ])
              let hints = root.GetProperty("resources").GetProperty("tag:me@example.com,2016:widgets").GetProperty "hints"

              let strings name =
                  hints.GetProperty(name: string).EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq

              Expect.equal (strings "acceptPost") [ "application/json" ] "acceptPost"
              Expect.equal (strings "acceptPut") [ "application/json"; "application/merge-patch+json" ] "acceptPut"

              let acceptHintNames =
                  hints.EnumerateObject()
                  |> Seq.map (fun p -> p.Name)
                  |> Seq.filter (fun n -> n.StartsWith "accept")
                  |> Set.ofSeq

              Expect.equal
                  acceptHintNames
                  (Set.ofList [ "acceptPost"; "acceptPut" ])
                  "GET's accepts entry does not surface as any accept* hint"
          }

          test "emits acceptRanges, acceptPrefer, preconditionRequired, and authSchemes" {
              let guarded =
                  { widgets with
                      AcceptRanges = [ "bytes" ]
                      AcceptPrefer = [ "return=minimal"; "wait" ]
                      PreconditionRequired = [ Precondition.ETag; Precondition.LastModified ]
                      AuthSchemes = [ "Basic", [ "private" ]; "Bearer", [] ] }

              let root = parse (JsonHome.serialize JsonHomeOptions.Default [ guarded ])

              let hints =
                  root.GetProperty("resources").GetProperty("tag:me@example.com,2016:widgets").GetProperty "hints"

              let strings name =
                  hints.GetProperty(name: string).EnumerateArray()
                  |> Seq.map (fun e -> e.GetString())
                  |> List.ofSeq

              Expect.equal (strings "acceptRanges") [ "bytes" ] "acceptRanges"
              Expect.equal (strings "acceptPrefer") [ "return=minimal"; "wait" ] "acceptPrefer"

              Expect.equal
                  (strings "preconditionRequired")
                  [ "etag"; "last-modified" ]
                  "preconditionRequired uses the draft's spellings"

              let schemes = hints.GetProperty("authSchemes").EnumerateArray() |> List.ofSeq
              Expect.hasLength schemes 2 "Two auth schemes"

              Expect.equal (schemes.[0].GetProperty("scheme").GetString()) "Basic" "First scheme"

              Expect.equal
                  (schemes.[0].GetProperty("realms").EnumerateArray()
                   |> Seq.map (fun e -> e.GetString())
                   |> List.ofSeq)
                  [ "private" ]
                  "First scheme's realms"

              Expect.equal (schemes.[1].GetProperty("scheme").GetString()) "Bearer" "Second scheme"

              // realms is optional, so a scheme without any omits it entirely.
              Expect.isFalse (fst (schemes.[1].TryGetProperty "realms")) "No empty realms array"
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
