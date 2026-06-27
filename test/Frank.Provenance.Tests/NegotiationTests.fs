module Frank.Provenance.Tests.NegotiationTests

open Microsoft.AspNetCore.Http
open Expecto
open Frank

let private ctxWithAccept (accept: string) =
    let ctx = DefaultHttpContext()
    ctx.Request.Headers.Append("Accept", accept)
    ctx :> HttpContext

[<Tests>]
let tests =
    testList
        "AcceptNegotiation.wantsProfile"
        [ test "exact prov profile returns true" {
              let ctx = ctxWithAccept "application/ld+json; profile=\"http://www.w3.org/ns/prov\""

              Expect.isTrue
                  (AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov")
                  "should match exact prov profile"
          }

          test "prov profile with explicit q=1.0 returns true" {
              let ctx =
                  ctxWithAccept "application/ld+json; profile=\"http://www.w3.org/ns/prov\"; q=1.0"

              Expect.isTrue
                  (AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov")
                  "q=1.0 is non-zero, should match"
          }

          test "prov profile with q=0 returns false" {
              let ctx =
                  ctxWithAccept "application/ld+json; profile=\"http://www.w3.org/ns/prov\"; q=0"

              Expect.isFalse
                  (AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov")
                  "q=0 excludes the type, should not match"
          }

          test "different profile (prov-dictionary) returns false" {
              let ctx =
                  ctxWithAccept "text/html, application/ld+json; profile=\"http://www.w3.org/ns/prov-dictionary\""

              Expect.isFalse
                  (AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov")
                  "prov-dictionary profile must not match prov"
          }

          test "no accept header returns false" {
              let ctx = DefaultHttpContext() :> HttpContext

              Expect.isFalse
                  (AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov")
                  "empty accept should not match"
          }

          test "application/json with no profile returns false" {
              let ctx = ctxWithAccept "application/json"

              Expect.isFalse
                  (AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov")
                  "wrong media type should not match"
          }

          test "prov hash fragment false-positive rejected" {
              let ctx =
                  ctxWithAccept "application/ld+json; profile=\"http://www.w3.org/ns/prov#Activity\""

              Expect.isFalse
                  (AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov")
                  "hash fragment variant must not match exact IRI"
          } ]
