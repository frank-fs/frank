/// #413: `frank semantic extract` must migrate its vocab fetch from VocabFetcher.httpFetch
/// (plain GET, no RDF negotiation) onto RdfConneg.rdfFetch — matching refresh/validate's
/// already-shipped pattern — so a vocab source serving non-RDF content (HTML, a redirect, a
/// 4xx page) reports a structured outcome instead of an opaque RDF-parser exception.
module Frank.Cli.Core.Tests.ExtractRdfConnegTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Reflection
open System.Threading.Tasks
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

// ── Loopback HTTP fixture (same pattern as ConnegFetcherTests.fs) ──────────────

/// Bind an HttpListener on a random port without TOCTOU.
/// Tries up to 20 random ports; raises invalidOp if none succeed (Holzmann #10).
let private bindHttpListener () : HttpListener * int =
    let mutable result = ValueNone
    let mutable attempt = 0

    while attempt < 20 && result.IsNone do
        let port = Random.Shared.Next(40000, 60000)
        let l = new HttpListener()
        l.Prefixes.Add $"http://localhost:{port}/"

        try
            l.Start()
            result <- ValueSome(l, port)
        with _ ->
            (l :> IDisposable).Dispose()
            attempt <- attempt + 1

    match result with
    | ValueNone -> invalidOp "could not bind HttpListener after 20 attempts"
    | ValueSome r -> r

/// Serve up to maxRequests requests using handler, then stop (cap-hit behavior: stop serving).
let private startServing (listener: HttpListener) (maxRequests: int) (handler: HttpListenerContext -> unit) : Task =
    Task.Run(fun () ->
        let mutable count = 0

        while count < maxRequests do
            try
                let ctx = listener.GetContextAsync().GetAwaiter().GetResult()

                try
                    handler ctx
                with _ ->
                    ()

                count <- count + 1
            with _ ->
                count <- maxRequests)

/// Run f with a loopback stub bound at a random port, serving up to maxRequests via handler.
let private withStub (maxRequests: int) (handler: HttpListenerContext -> unit) (f: Uri -> 'T) : 'T =
    let listener, port = bindHttpListener ()
    let baseUri = Uri $"http://localhost:{port}/"
    let _ = startServing listener maxRequests handler

    try
        f baseUri
    finally
        try
            listener.Stop()
        with _ ->
            ()

        // Listener disposal races the shared static EndPointManager under solution-wide
        // parallel test runs (many HttpListener-backed test files disposing concurrently) —
        // an "Address already in use" from HttpListener.Dispose() here is a benign cleanup
        // race, not a test failure; the test's own assertions already ran to completion above.
        try
            (listener :> IDisposable).Dispose()
        with _ ->
            ()

let private respond (ctx: HttpListenerContext) (status: int) (contentType: string) (body: byte[]) : unit =
    ctx.Response.StatusCode <- status
    ctx.Response.ContentType <- contentType
    ctx.Response.ContentLength64 <- int64 body.Length
    use stream = ctx.Response.OutputStream
    stream.Write(body, 0, body.Length)

let private htmlBytes =
    Text.Encoding.UTF8.GetBytes "<html><body>schema.org documentation page — not RDF</body></html>"

// ── Fixture project ─────────────────────────────────────────────────────────────

let private frankSemanticDllPath () =
    Assembly.GetAssembly(typeof<VocabularyRegistry>).Location

let private fsharpCoreDllPath () =
    Assembly.GetAssembly(typeof<int list>).Location

let private dllRefs () =
    [ frankSemanticDllPath (); fsharpCoreDllPath () ]

/// Writes a fixture project with `using "ex"` bound to the given vocab base URI, so the
/// pipeline puts "ex" in inScopePrefixes and actually invokes the injected fetch.
let private writeFixtureProject (tmpDir: string) (vocabBaseUri: string) : string * string =
    let domainSource =
        """namespace FixtureApp

type Widget = { Id: int; Name: string }
"""

    let vocabSource =
        $"""module Vocabulary
open Frank.Semantic

let registry =
    vocabulary {{
        prefix "ex" "{vocabBaseUri}"
        using "ex"
    }}
"""

    File.WriteAllText(Path.Combine(tmpDir, "Domain.fs"), domainSource)
    File.WriteAllText(Path.Combine(tmpDir, "Vocabulary.fs"), vocabSource)

    let fsprojContent =
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Domain.fs" />
    <Compile Include="Vocabulary.fs" />
  </ItemGroup>
</Project>
"""

    let projectFile = Path.Combine(tmpDir, "FixtureApp.fsproj")
    File.WriteAllText(projectFile, fsprojContent)
    let lockFilePath = Path.Combine(tmpDir, ".frank", "semantic-mappings.lock.json")
    projectFile, lockFilePath

let private makeClient () : HttpClient = RdfConneg.makeNoRedirectClient ()

let private withTempDir (f: string -> 'T) : 'T =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory tmpDir |> ignore

    try
        f tmpDir
    finally
        Directory.Delete(tmpDir, true)

// ── AC1: HTML-serving vocab source → structured outcome, not a parse crash ────

[<Tests>]
let ac1UnverifiableNonRdfTests =
    testList
        "413 AC1 — extract against a vocab source serving HTML reports UnverifiableNonRdf, not a raw parse crash"
        [ test "real HTTP fixture always serving text/html → Error is classified unverifiable-non-rdf" {
              withStub 1 (fun ctx -> respond ctx 200 "text/html" htmlBytes) (fun baseUri ->
                  withTempDir (fun tmpDir ->
                      let projectFile, _ = writeFixtureProject tmpDir (baseUri.AbsoluteUri)

                      use client = makeClient ()

                      let result =
                          Pipeline.runWithFetch
                              (RdfConneg.rdfFetch client)
                              (fun () -> DateTimeOffset.UtcNow)
                              { ProjectFile = projectFile
                                VocabularyFile = None
                                AssemblyRefs = dllRefs ()
                                OutputFormat = Pipeline.Text }

                      match result with
                      | Ok _ -> failtest "expected extract to fail against an HTML-only vocab source"
                      | Error msg ->
                          Expect.stringContains
                              msg
                              "unverifiable-non-rdf"
                              "error must carry the UnverifiableNonRdf classification marker"

                          Expect.isFalse
                              (msg.Contains "Unexpected character" || msg.Contains "parse failed (JsonLd)")
                              "must not be a raw RDF-parser exception message — the bug this issue fixes"))
          } ]

// ── AC2: same classification as refresh/validate would report for the identical endpoint ──

[<Tests>]
let ac2SameClassificationAsRefreshValidateTests =
    testList
        "413 AC2 — extract reports the same structured outcome refresh/validate would report"
        [ test
              "HTML-only endpoint (schema.org's actual observed root behavior) → same UnverifiableNonRdf RdfConneg.buildEvidence would report" {
              // 2 requests: one direct probe (to compute the expected refresh/validate reason)
              // and one through Pipeline.runWithFetch.
              withStub 2 (fun ctx -> respond ctx 200 "text/html" htmlBytes) (fun baseUri ->
                  withTempDir (fun tmpDir ->
                      let projectFile, _ = writeFixtureProject tmpDir (baseUri.AbsoluteUri)

                      use client = makeClient ()

                      // What refresh/validate would report for the identical endpoint.
                      let directResult =
                          RdfConneg.rdfFetch client baseUri None None |> Async.RunSynchronously

                      let directEvidence =
                          RdfConneg.buildEvidence baseUri DateTimeOffset.UtcNow directResult

                      let expectedReason =
                          match directEvidence with
                          | UnverifiableNonRdf reason -> reason
                          | other -> failtest $"test setup: expected UnverifiableNonRdf, got {other}"

                      let result =
                          Pipeline.runWithFetch
                              (RdfConneg.rdfFetch client)
                              (fun () -> DateTimeOffset.UtcNow)
                              { ProjectFile = projectFile
                                VocabularyFile = None
                                AssemblyRefs = dllRefs ()
                                OutputFormat = Pipeline.Text }

                      match result with
                      | Ok _ -> failtest "expected extract to fail against an HTML-only vocab source"
                      | Error msg ->
                          Expect.stringContains
                              msg
                              expectedReason
                              "extract's reported reason must match refresh/validate's buildEvidence classification"))
          }

          test
              "extract SUCCEEDS when the source properly content-negotiates RDF (regression: migration preserves the happy path)" {
              let turtleBytes (ns: string) =
                  Text.Encoding.UTF8.GetBytes
                      $"@prefix ex: <{ns}> .\n<{ns}Widget> a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

              let handler (ctx: HttpListenerContext) =
                  let accept = ctx.Request.Headers.Get "Accept"
                  let wantsRdf = accept <> null && accept.Contains "text/turtle"
                  let ns = ctx.Request.Url.GetLeftPart(UriPartial.Authority) + "/"

                  if wantsRdf then
                      respond ctx 200 "text/turtle" (turtleBytes ns)
                  else
                      respond ctx 200 "text/html" htmlBytes

              withStub 1 handler (fun baseUri ->
                  withTempDir (fun tmpDir ->
                      let projectFile, lockFilePath = writeFixtureProject tmpDir (baseUri.AbsoluteUri)

                      use client = makeClient ()

                      let result =
                          Pipeline.runWithFetch
                              (RdfConneg.rdfFetch client)
                              (fun () -> DateTimeOffset.UtcNow)
                              { ProjectFile = projectFile
                                VocabularyFile = None
                                AssemblyRefs = dllRefs ()
                                OutputFormat = Pipeline.Text }

                      Expect.isOk result "extract must succeed when the source properly RDF-negotiates"
                      let lf = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)
                      let entry = Map.tryFind "ex" lf.Vocabularies
                      Expect.isSome entry "Vocabularies must contain 'ex' prefix after a successful fetch"))
          } ]

// ── AC3: #nowarn "44" and VocabFetcher.httpFetch are gone from Pipeline.fs ─────

[<Tests>]
let ac3SourceNoLongerReferencesHttpFetchTests =
    testList
        "413 AC3 — Pipeline.fs no longer suppresses obsolete warnings or calls VocabFetcher.httpFetch"
        [ test "Pipeline.fs source text contains neither #nowarn \"44\" nor VocabFetcher.httpFetch" {
              let pipelineSourcePath =
                  Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Frank.Cli.Core", "Pipeline.fs")

              Expect.isTrue (File.Exists pipelineSourcePath) $"Pipeline.fs must exist at {pipelineSourcePath}"
              let source = File.ReadAllText pipelineSourcePath

              Expect.isFalse (source.Contains "#nowarn \"44\"") "#nowarn \"44\" must be removed from Pipeline.fs"

              Expect.isFalse
                  (source.Contains "VocabFetcher.httpFetch")
                  "Pipeline.fs must no longer reference VocabFetcher.httpFetch"
          } ]

// ── Cache-hit regression: the conneg fetch path still honors the on-disk cache ─

[<Tests>]
let cacheHitRegressionTests =
    testList
        "413 — fetchAndCacheConneg cache-hit path: a second extract does not re-fetch"
        [ test "second extract run against the same project dir does not re-invoke the network fetch" {
              let turtleBytes (ns: string) =
                  Text.Encoding.UTF8.GetBytes
                      $"@prefix ex: <{ns}> .\n<{ns}Widget> a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

              let requestCount = ref 0

              let handler (ctx: HttpListenerContext) =
                  System.Threading.Interlocked.Increment requestCount |> ignore
                  let ns = ctx.Request.Url.GetLeftPart(UriPartial.Authority) + "/"
                  respond ctx 200 "text/turtle" (turtleBytes ns)

              withStub 1 handler (fun baseUri ->
                  withTempDir (fun tmpDir ->
                      let projectFile, _ = writeFixtureProject tmpDir (baseUri.AbsoluteUri)

                      use client = makeClient ()
                      let fetch = RdfConneg.rdfFetch client

                      let opts: Pipeline.ExtractOptions =
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                      let result1 = Pipeline.runWithFetch fetch (fun () -> DateTimeOffset.UtcNow) opts
                      Expect.isOk result1 "first extract must succeed"

                      // Second run must hit the on-disk cache, not the network — the stub only
                      // serves 1 request (withStub 1); a second network call would time out
                      // GetContextAsync forever if this weren't cache-satisfied. Passing this
                      // synchronously proves no second request was attempted.
                      let result2 = Pipeline.runWithFetch fetch (fun () -> DateTimeOffset.UtcNow) opts
                      Expect.isOk result2 "second extract must succeed from cache"

                      Expect.equal
                          requestCount.Value
                          1
                          "exactly one network request — the second extract must be a cache hit"))
          } ]
