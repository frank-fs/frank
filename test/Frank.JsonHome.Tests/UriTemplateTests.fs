module Frank.JsonHome.Tests.UriTemplateTests

open Expecto
open Frank.JsonHome

[<Tests>]
let tests =
    testList
        "UriTemplate"
        [ test "translates ASP.NET route templates to RFC 6570" {
              let cases =
                  [ "/products", "/products"
                    "/products/{id}", "/products/{id}"
                    "/products/{id:guid}", "/products/{id}"
                    "/products/{id:minlength(4)}", "/products/{id}"
                    "/products/{id?}", "/products/{id}"
                    "/products/{id=1}", "/products/{id}"
                    "/files/{*path}", "/files/{+path}"
                    "/files/{**path}", "/files/{+path}"
                    "/a/{x}/b/{y:int}", "/a/{x}/b/{y}" ]

              for input, expected in cases do
                  Expect.equal (UriTemplate.ofRouteTemplate input) expected ("Translating " + input)
          }

          test "extracts variable names" {
              Expect.equal (UriTemplate.variables "/a/{x}/b/{y:int}") [ "x"; "y" ] "Names without constraints"
              Expect.equal (UriTemplate.variables "/files/{*path}") [ "path" ] "Catch-all name without star"
              Expect.equal (UriTemplate.variables "/products") [] "No variables"
          }

          test "detects templated routes" {
              Expect.isTrue (UriTemplate.isTemplated "/products/{id}") "Has a variable"
              Expect.isFalse (UriTemplate.isTemplated "/products") "No variables"
          } ]
