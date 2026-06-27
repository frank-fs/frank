module Frank.Validation.Tests.MiddlewareTests

open System.Net.Http
open System.Text
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Validation.Tests.MiddlewareTestHelpers

let private validOrderBody =
    """{
  "@context": "https://schema.org",
  "@type": "Order",
  "@id": "https://example.org/order/1",
  "totalPaymentDue": {"@value": "100", "@type": "http://www.w3.org/2001/XMLSchema#decimal"}
}"""

let private invalidOrderBody =
    """{
  "@context": "https://schema.org",
  "@type": "Order",
  "@id": "https://example.org/order/1",
  "totalPaymentDue": "not-a-number"
}"""

let private missingPropertyBody =
    """{
  "@context": "https://schema.org",
  "@type": "Order",
  "@id": "https://example.org/order/1"
}"""

let private malformedJsonLdBody = "{ not json"

let private postLdJson (client: HttpClient) (body: string) : HttpResponseMessage =
    let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
    client.PostAsync("/echo", content).GetAwaiter().GetResult()

[<Tests>]
let tests =
    testList
        "ValidationMiddleware (TestServer)"
        [ testCase "POST ld+json valid body passes through to handler (200)"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client validOrderBody
              Expect.equal (int resp.StatusCode) 200 "valid ld+json passes through to handler"

          testCase "POST ld+json invalid datatype returns 422"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client invalidOrderBody
              Expect.equal (int resp.StatusCode) 422 "invalid datatype returns 422 Unprocessable"

          testCase "POST ld+json invalid datatype returns Content-Type application/ld+json with SHACL profile"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client invalidOrderBody
              let ct = resp.Content.Headers.ContentType.ToString()
              Expect.stringContains ct "application/ld+json" "422 body is ld+json"
              Expect.stringContains ct "profile=\"http://www.w3.org/ns/shacl#\"" "422 Content-Type includes SHACL profile"

          testCase "POST ld+json invalid datatype report body contains schema.org/totalPaymentDue"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client invalidOrderBody
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "schema.org/totalPaymentDue" "report references property IRI"

          testCase "POST ld+json invalid datatype report body has NO urn:frank:"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client invalidOrderBody
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.isFalse (body.Contains("urn:frank:")) "report must not contain urn:frank: IRIs"

          testCase "POST ld+json missing required property returns 422"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client missingPropertyBody
              Expect.equal (int resp.StatusCode) 422 "missing required property returns 422"

          testCase "POST application/json plain (not ld+json) passes through (200)"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let content = new StringContent("""{"foo":"bar"}""", Encoding.UTF8, "application/json")
              let (resp: HttpResponseMessage) = client.PostAsync("/echo", content).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "plain JSON passes through"

          testCase "GET passes through (200)"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = client.GetAsync("/echo").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "GET passes through"

          testCase "POST ld+json body-rewind: handler sees full body after validation"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client validOrderBody
              Expect.equal (int resp.StatusCode) 200 "passed through"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body (string validOrderBody.Length) "handler read exact byte count (body was rewound)"

          testCase "POST ld+json with unknown @context → synthesizing loader fails-closed → 400"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let unknownContextBody =
                  """{
  "@context": "http://example.com/unknown",
  "@type": "Order",
  "totalPaymentDue": {"@value": "100", "@type": "http://www.w3.org/2001/XMLSchema#decimal"}
}"""
              let (resp: HttpResponseMessage) = postLdJson client unknownContextBody
              Expect.equal (int resp.StatusCode) 400 "unknown @context IRI causes parse failure → 400 (fail-closed)"

          testCase "POST ld+json body exceeding MaxBodyBytes returns 413"
          <| fun _ ->
              let config =
                  { orderConfig () with MaxBodyBytes = 64L }

              use app = startValidationServer config
              use client = app.GetTestClient()
              let largeBody = validOrderBody + System.String(' ', 256)
              let (resp: HttpResponseMessage) = postLdJson client largeBody
              Expect.equal (int resp.StatusCode) 413 "oversized body returns 413 Payload Too Large"

          testCase "POST malformed JSON-LD returns 400 with application/problem+json Content-Type"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client malformedJsonLdBody
              Expect.equal (int resp.StatusCode) 400 "malformed JSON-LD returns 400"
              let ct = resp.Content.Headers.ContentType.MediaType
              Expect.stringContains ct "application/problem+json" "400 response is application/problem+json"

          testCase "POST ld+json invalid body 422 has Link: describedby SHACL header"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client invalidOrderBody
              Expect.equal (int resp.StatusCode) 422 "422 status"
              let hasLink =
                  resp.Headers.TryGetValues("Link")
                  |> function
                      | true, vals ->
                          vals
                          |> Seq.exists (fun v ->
                              v.Contains("rel=\"describedby\"") && v.Contains("shacl#"))
                      | _ -> false

              Expect.isTrue hasLink "422 response has Link header with rel=describedby pointing to SHACL"

          testCase "POST ld+json invalid body 422 report contains @context and sh prefix"
          <| fun _ ->
              let config = orderConfig ()
              use app = startValidationServer config
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = postLdJson client invalidOrderBody
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "@context" "422 report has @context"
              Expect.stringContains body "shacl#" "422 report context includes shacl# namespace" ]
