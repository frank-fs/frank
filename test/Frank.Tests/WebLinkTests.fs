module Frank.Tests.WebLinkTests

open Expecto
open Frank.Builder

[<Tests>]
let webLinkFormatTests =
    testList "WebLink.format" [
        testCase "a link with no params formats as target and rel only" (fun () ->
            let link = { Target = "/.well-known/home.json"; Rel = "home"; Params = [] }
            Expect.equal (WebLink.format link) "</.well-known/home.json>; rel=\"home\"" "No trailing params")

        testCase "a link with one param appends it as a quoted attribute" (fun () ->
            let link =
                { Target = "/.well-known/openapi.json"
                  Rel = "service-desc"
                  Params = [ "type", "application/json" ] }
            Expect.equal
                (WebLink.format link)
                "</.well-known/openapi.json>; rel=\"service-desc\"; type=\"application/json\""
                "One param appended")

        testCase "a link with multiple params appends them in order" (fun () ->
            let link =
                { Target = "/x"
                  Rel = "alternate"
                  Params = [ "type", "application/ld+json"; "title", "JSON-LD" ] }
            Expect.equal
                (WebLink.format link)
                "</x>; rel=\"alternate\"; type=\"application/ld+json\"; title=\"JSON-LD\""
                "Params appended in declaration order")

        testCase "a backslash in a param value is escaped" (fun () ->
            let link = { Target = "/x"; Rel = "alternate"; Params = [ "title", "back\\slash" ] }
            Expect.equal
                (WebLink.format link)
                "</x>; rel=\"alternate\"; title=\"back\\\\slash\""
                "Backslash doubled")

        testCase "a double quote in a param value is escaped" (fun () ->
            let link = { Target = "/x"; Rel = "alternate"; Params = [ "title", "say \"hi\"" ] }
            Expect.equal
                (WebLink.format link)
                "</x>; rel=\"alternate\"; title=\"say \\\"hi\\\"\""
                "Double quote escaped")

        testCase "a backslash or quote in rel itself is escaped" (fun () ->
            let link = { Target = "/x"; Rel = "weird\"rel"; Params = [] }
            Expect.equal (WebLink.format link) "</x>; rel=\"weird\\\"rel\"" "Rel escaped too")
    ]
