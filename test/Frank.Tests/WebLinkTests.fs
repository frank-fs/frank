module Frank.Tests.WebLinkTests

open Expecto
open Frank.Builder

[<Tests>]
let tests =
    testList
        "WebLink"
        [ test "format emits an RFC 8288 field value" {
              let link = WebLink.create "/.well-known/home.json" "home"

              Expect.equal
                  (WebLink.format link)
                  "</.well-known/home.json>; rel=\"home\""
                  "Target is bracketed and rel is quoted"
          }

          test "format appends quoted parameters in order" {
              let link =
                  { WebLink.create "/docs" "service-doc" with
                      Params = [ "title", "Docs"; "type", "text/html" ] }

              Expect.equal
                  (WebLink.format link)
                  "</docs>; rel=\"service-doc\"; title=\"Docs\"; type=\"text/html\""
                  "Parameters follow rel in declaration order"
          }

          test "format escapes quotes and backslashes in parameter values" {
              let link =
                  { WebLink.create "/x" "about" with
                      Params = [ "title", "a \"quoted\" c:\\path" ] }

              Expect.equal
                  (WebLink.format link)
                  "</x>; rel=\"about\"; title=\"a \\\"quoted\\\" c:\\\\path\""
                  "Backslashes and quotes are escaped"
          } ]
