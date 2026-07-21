module Frank.Provenance.Tests.EndpointTests

open System
open System.IO
open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Provenance

let private mkRecord id resource =
    { Id = id
      ResourceUri = resource
      HttpMethod = "GET"
      StatusCode = 200
      DomainType = None
      Agent = { Id = "urn:agent:anon"; Label = None }
      StartedAt = DateTimeOffset.UnixEpoch
      EndedAt = DateTimeOffset.UnixEpoch
      BodyAttributes = [] }

/// #412: extract the set of @context prefix keys present in a JSON-LD body.
let private ctxKeys (body: string) : Set<string> =
    use doc = JsonDocument.Parse body
    let root = doc.RootElement
    let mutable ctxEl = Unchecked.defaultof<JsonElement>

    if not (root.TryGetProperty("@context", &ctxEl)) then
        Set.empty
    else
        let keysOf (el: JsonElement) =
            if el.ValueKind = JsonValueKind.Object then
                el.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Set.ofSeq
            else
                Set.empty

        match ctxEl.ValueKind with
        | JsonValueKind.Object -> keysOf ctxEl
        | JsonValueKind.Array -> ctxEl.EnumerateArray() |> Seq.map keysOf |> Seq.fold Set.union Set.empty
        | _ -> Set.empty

let private countOccurrences (sub: string) (s: string) =
    let mutable count = 0
    let mutable idx = 0

    while idx <= s.Length - sub.Length do
        if s.[idx .. idx + sub.Length - 1] = sub then
            count <- count + 1
            idx <- idx + sub.Length
        else
            idx <- idx + 1

    count

let private defaultConfig: ProvenanceConfig =
    { ProvClasses = Map.empty
      KnownNamespaces = [||]
      PropertyClassRanges = Map.empty
      DeclaredPrefixes = []
      StoreConfig = ProvenanceStoreConfig.defaults
      MaxBodyBytes = ProvenanceConfig.defaultMaxBodyBytes }

let private startEndpointServer () =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore

    let store =
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

    builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
    builder.Services.AddSingleton<ProvenanceConfig>(defaultConfig) |> ignore
    let app = builder.Build()
    let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()
    resolvedStore.Append(mkRecord "urn:uuid:act-1" "http://localhost/r")
    resolvedStore.Append(mkRecord "urn:uuid:act-2" "http://localhost/r")

    app.MapGet(
        "/provenance",
        Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore defaultConfig)
    )
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

/// Config with schema+ttt declared prefixes — matches what TicTacToe generates.
let private tttConfig: ProvenanceConfig =
    { defaultConfig with
        DeclaredPrefixes = [ "schema", "https://schema.org/"; "ttt", "/tictactoe#" ] }

/// Server whose records carry a schema:agent body attribute (used prefix test) and a ttt IRI
/// node (used ttt prefix test). Both prefixes appear in the merged graph so both appear in
/// @context under the emit-only-used semantics.
let private startEndpointServerWithSchemaAttrs () =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore

    let store =
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

    builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
    builder.Services.AddSingleton<ProvenanceConfig>(tttConfig) |> ignore
    let app = builder.Build()
    let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

    let mkWithAttr id resource =
        { mkRecord id resource with
            BodyAttributes =
                [ "https://schema.org/agent", Literal "alice"
                  "http://localhost/tictactoe#square", IriNode "http://localhost/tictactoe#TopLeft" ] }

    resolvedStore.Append(mkWithAttr "urn:uuid:act-1" "http://localhost/r")
    resolvedStore.Append(mkWithAttr "urn:uuid:act-2" "http://localhost/r")

    app.MapGet(
        "/provenance",
        Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore tttConfig)
    )
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

/// #412: server exposing both /provenance and /provenance/{nodeId}, seeded with the given
/// records under resource "http://localhost/r". Records' Id must be "http://localhost/provenance/<suffix>"
/// to route through handleActivityNode via nodeId=<suffix>.
let private startNodeServer (records: ProvenanceRecord list) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore

    let store =
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

    builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
    builder.Services.AddSingleton<ProvenanceConfig>(defaultConfig) |> ignore
    let app = builder.Build()
    let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

    for r in records do
        resolvedStore.Append r

    app.MapGet(
        "/provenance",
        Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore defaultConfig)
    )
    |> ignore

    app.MapGet(
        "/provenance/{nodeId}",
        Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handleNode resolvedStore defaultConfig)
    )
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let tests =
    testList
        "ProvenanceEndpoint"
        [ testCaseAsync "GET /provenance?resource=/r returns 200 ld+json with two Activity entries"
          <| async {
              use app = startEndpointServer ()
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance?resource=/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"

              Expect.isTrue
                  (resp.Content.Headers.ContentType.MediaType.StartsWith("application/ld+json"))
                  "content-type is ld+json"

              let count = countOccurrences "prov:Activity" body

              Expect.isGreaterThanOrEqual count 2 "at least two prov:Activity CURIEs in body"
          }

          testCaseAsync "GET /provenance without resource param returns 400 problem+json"
          <| async {
              use app = startEndpointServer ()
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance") |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 400 "status 400"

              Expect.equal
                  resp.Content.Headers.ContentType.MediaType
                  "application/problem+json"
                  "content-type is problem+json"

              Expect.stringContains body "Missing required query parameter" "title in body"
          }

          testCaseAsync "#16 provenance endpoint @context includes schema and ttt prefixes"
          <| async {
              // Use a server with schema body attrs so compaction is observable.
              use app = startEndpointServerWithSchemaAttrs ()
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              let mutable schemaEl = Unchecked.defaultof<JsonElement>
              let mutable tttEl = Unchecked.defaultof<JsonElement>
              use doc = JsonDocument.Parse body
              let root = doc.RootElement

              let tryGetCtxObj () =
                  let mutable ctxEl = Unchecked.defaultof<JsonElement>

                  if root.TryGetProperty("@context", &ctxEl) then
                      match ctxEl.ValueKind with
                      | JsonValueKind.Object -> Some ctxEl
                      | JsonValueKind.Array ->
                          ctxEl.EnumerateArray()
                          |> Seq.tryFind (fun e -> e.ValueKind = JsonValueKind.Object)
                      | _ -> None
                  else
                      None

              let ctxObj = tryGetCtxObj ()
              Expect.isSome ctxObj "@context object must be present"
              let obj = ctxObj.Value
              Expect.isTrue (obj.TryGetProperty("schema", &schemaEl)) "@context has 'schema' prefix"
              Expect.isTrue (obj.TryGetProperty("ttt", &tttEl)) "@context has 'ttt' prefix"
              // #16 real compaction: schema body attr must compact when schema is in extraContext.
              Expect.stringContains body "schema:agent" "schema:agent must be compacted in provenance body"

              Expect.isFalse
                  (body.Contains "\"https://schema.org/agent\"")
                  "full schema.org agent IRI must not appear as JSON property key after compaction"
          }

          testCaseAsync "D malformed Host header returns 400 (consistent with middlewares)"
          <| async {
              // RED before Fix D: endpoint computed scheme+host without validation → UriFormatException / 500.
              // GREEN after Fix D: tryValidateOrigin catches malformed host, returns 400.
              let builder = WebApplication.CreateBuilder()
              builder.WebHost.UseTestServer() |> ignore

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
                  :> IProvenanceStore

              builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(defaultConfig) |> ignore
              let app = builder.Build()
              let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

              app.MapGet(
                  "/provenance",
                  Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore defaultConfig)
              )
              |> ignore

              app.StartAsync().GetAwaiter().GetResult()
              use app = app
              let server = app.GetTestServer()

              let! ctx =
                  server.SendAsync(
                      Action<HttpContext>(fun ctx ->
                          ctx.Request.Method <- "GET"
                          ctx.Request.Scheme <- "http"
                          ctx.Request.Host <- HostString "ex ample.com"
                          ctx.Request.Path <- PathString "/provenance"
                          ctx.Request.QueryString <- QueryString "?resource=/r")
                  )
                  |> Async.AwaitTask

              Expect.equal ctx.Response.StatusCode 400 "malformed Host → 400"
          }

          testCaseAsync "F declared-vocab threading — non-ttt config produces no ttt in @context"
          <| async {
              // RED before Fix F: endpoint hardcoded ttt regardless of config.
              // GREEN after Fix F (v2): only USED DeclaredPrefixes entries appear in @context.
              // Records carry body attrs that use both schema and ex so both appear under
              // the emit-only-used semantics introduced by the Fix F v2 re-implementation.
              let exConfig =
                  { defaultConfig with
                      DeclaredPrefixes = [ "schema", "https://schema.org/"; "ex", "/ex#" ] }

              let builder = WebApplication.CreateBuilder()
              builder.WebHost.UseTestServer() |> ignore

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
                  :> IProvenanceStore

              builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(exConfig) |> ignore
              let app = builder.Build()
              let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

              let mkWithSchemaAndEx id resource =
                  { mkRecord id resource with
                      BodyAttributes =
                          [ "https://schema.org/name", Literal "test"
                            "http://localhost/ex#foo", Literal "bar" ] }

              resolvedStore.Append(mkWithSchemaAndEx "urn:uuid:act-x" "http://localhost/r")

              app.MapGet(
                  "/provenance",
                  Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore exConfig)
              )
              |> ignore

              app.StartAsync().GetAwaiter().GetResult()
              use app = app
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"

              use doc = JsonDocument.Parse body
              let mutable ctxEl = Unchecked.defaultof<JsonElement>
              let root = doc.RootElement

              let tryGetCtxProp (name: string) =
                  if root.TryGetProperty("@context", &ctxEl) then
                      let mutable propEl = Unchecked.defaultof<JsonElement>

                      let tryIn (el: JsonElement) =
                          if el.ValueKind = JsonValueKind.Object then
                              el.TryGetProperty(name, &propEl)
                          else
                              false

                      match ctxEl.ValueKind with
                      | JsonValueKind.Object -> tryIn ctxEl
                      | JsonValueKind.Array -> ctxEl.EnumerateArray() |> Seq.exists tryIn
                      | _ -> false
                  else
                      false

              Expect.isTrue (tryGetCtxProp "schema") "@context must have 'schema' (from DeclaredPrefixes)"
              Expect.isTrue (tryGetCtxProp "ex") "@context must have 'ex' (from DeclaredPrefixes)"
              Expect.isFalse (tryGetCtxProp "ttt") "@context must NOT have 'ttt' — not in DeclaredPrefixes"
              Expect.isFalse (body.Contains "tictactoe") "body must not contain 'tictactoe' — not in DeclaredPrefixes"
          }

          testCaseAsync "F-v2 unused external prefix is omitted from @context"
          <| async {
              // RED before fix: current endpoint emits ALL declared prefixes regardless of usage,
              // so 'wikidata' appears in @context even though no provenance term uses it.
              // GREEN after fix: only prefixes whose namespace prefixes a graph URI node are emitted.
              let wikidataConfig =
                  { defaultConfig with
                      DeclaredPrefixes =
                          [ "schema", "https://schema.org/"
                            "wikidata", "http://www.wikidata.org/entity/" ] }

              let builder = WebApplication.CreateBuilder()
              builder.WebHost.UseTestServer() |> ignore

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
                  :> IProvenanceStore

              builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(wikidataConfig) |> ignore
              let app = builder.Build()
              let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

              let mkWithSchema id resource =
                  { mkRecord id resource with
                      BodyAttributes = [ "https://schema.org/agent", Literal "alice" ] }

              resolvedStore.Append(mkWithSchema "urn:uuid:act-w1" "http://localhost/r")

              app.MapGet(
                  "/provenance",
                  Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore wikidataConfig)
              )
              |> ignore

              app.StartAsync().GetAwaiter().GetResult()
              use app = app
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              use doc = JsonDocument.Parse body

              let hasCtxProp (name: string) =
                  let mutable ctxEl = Unchecked.defaultof<JsonElement>
                  let root = doc.RootElement

                  if root.TryGetProperty("@context", &ctxEl) then
                      let tryIn (el: JsonElement) =
                          if el.ValueKind = JsonValueKind.Object then
                              let mutable propEl = Unchecked.defaultof<JsonElement>
                              el.TryGetProperty(name, &propEl)
                          else
                              false

                      match ctxEl.ValueKind with
                      | JsonValueKind.Object -> tryIn ctxEl
                      | JsonValueKind.Array -> ctxEl.EnumerateArray() |> Seq.exists tryIn
                      | _ -> false
                  else
                      false

              Expect.isTrue (hasCtxProp "schema") "@context must have 'schema' (schema body attr used)"
              Expect.isFalse (hasCtxProp "wikidata") "@context must NOT have 'wikidata' — no wikidata term in graph"
          }

          testCaseAsync "F-v2 used external prefix resolves to absolute namespace even when stored host-relative"
          <| async {
              // THE HOLE CASE: ProvenanceEmitter.toStoredNs may misclassify an external prefix
              // (e.g. 'wikidata') as app-owned when it appears in a mapping IRI, stripping it
              // to the host-relative form '/entity/'. The current endpoint then resolves that to
              // '<origin>/entity/' = 'http://localhost/entity/' — WRONG.
              //
              // RED: current endpoint resolves '/entity/' → 'http://localhost/entity/', which is not
              // 'http://www.wikidata.org/entity/'. GREEN: new path-based approach sees the graph URI
              // 'http://www.wikidata.org/entity/Q11416' whose path '/entity/Q11416' starts with
              // '/entity/', derives the absolute namespace from that URI's scheme+host:
              // 'http://www.wikidata.org' + '/entity/' = 'http://www.wikidata.org/entity/'.
              let corruptedConfig =
                  { defaultConfig with
                      DeclaredPrefixes = [ "wikidata", "/entity/" ] }

              let builder = WebApplication.CreateBuilder()
              builder.WebHost.UseTestServer() |> ignore

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
                  :> IProvenanceStore

              builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(corruptedConfig) |> ignore
              let app = builder.Build()
              let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

              let mkWikidataRecord id resource =
                  { mkRecord id resource with
                      BodyAttributes = [ "https://schema.org/about", IriNode "http://www.wikidata.org/entity/Q11416" ] }

              resolvedStore.Append(mkWikidataRecord "urn:uuid:act-wd" "http://localhost/r")

              app.MapGet(
                  "/provenance",
                  Func<HttpContext, System.Threading.Tasks.Task>(
                      ProvenanceEndpoint.handle resolvedStore corruptedConfig
                  )
              )
              |> ignore

              app.StartAsync().GetAwaiter().GetResult()
              use app = app
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              use doc = JsonDocument.Parse body
              let root = doc.RootElement

              let getCtxPropValue (name: string) =
                  let mutable ctxEl = Unchecked.defaultof<JsonElement>

                  if root.TryGetProperty("@context", &ctxEl) then
                      let tryGet (el: JsonElement) =
                          if el.ValueKind = JsonValueKind.Object then
                              let mutable propEl = Unchecked.defaultof<JsonElement>

                              if el.TryGetProperty(name, &propEl) then
                                  Some(propEl.GetString())
                              else
                                  None
                          else
                              None

                      match ctxEl.ValueKind with
                      | JsonValueKind.Object -> tryGet ctxEl
                      | JsonValueKind.Array -> ctxEl.EnumerateArray() |> Seq.tryPick tryGet
                      | _ -> None
                  else
                      None

              let wikidataValue = getCtxPropValue "wikidata"
              Expect.isSome wikidataValue "'wikidata' must be present in @context (term IS used)"

              Expect.equal
                  wikidataValue.Value
                  "http://www.wikidata.org/entity/"
                  "wikidata namespace must be the absolute IRI derived from the graph URI, not <origin>/entity/"

              Expect.isFalse (body.Contains "localhost/entity/") "corrupted <origin>/entity/ must NOT appear"
          }

          testCaseAsync "F-v2 app-owned ttt prefix host-resolves and absolute placeholder is absent (discriminating)"
          <| async {
              // Discriminating: prior tests checked Contains("/tictactoe#") which would also pass
              // for the absolute placeholder "https://example.org/tictactoe#". This test checks
              // the exact VALUE of 'ttt' in @context is the origin-resolved form and asserts
              // the placeholder is absent.
              let tttOnlyConfig =
                  { defaultConfig with
                      DeclaredPrefixes = [ "ttt", "/tictactoe#" ] }

              let builder = WebApplication.CreateBuilder()
              builder.WebHost.UseTestServer() |> ignore

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
                  :> IProvenanceStore

              builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(tttOnlyConfig) |> ignore
              let app = builder.Build()
              let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

              let mkTttRecord id resource =
                  { mkRecord id resource with
                      BodyAttributes =
                          [ "http://localhost/tictactoe#square", IriNode "http://localhost/tictactoe#TopLeft" ] }

              resolvedStore.Append(mkTttRecord "urn:uuid:act-ttt" "http://localhost/r")

              app.MapGet(
                  "/provenance",
                  Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore tttOnlyConfig)
              )
              |> ignore

              app.StartAsync().GetAwaiter().GetResult()
              use app = app
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              use doc = JsonDocument.Parse body
              let root = doc.RootElement

              let getCtxPropValue (name: string) =
                  let mutable ctxEl = Unchecked.defaultof<JsonElement>

                  if root.TryGetProperty("@context", &ctxEl) then
                      let tryGet (el: JsonElement) =
                          if el.ValueKind = JsonValueKind.Object then
                              let mutable propEl = Unchecked.defaultof<JsonElement>

                              if el.TryGetProperty(name, &propEl) then
                                  Some(propEl.GetString())
                              else
                                  None
                          else
                              None

                      match ctxEl.ValueKind with
                      | JsonValueKind.Object -> tryGet ctxEl
                      | JsonValueKind.Array -> ctxEl.EnumerateArray() |> Seq.tryPick tryGet
                      | _ -> None
                  else
                      None

              let tttValue = getCtxPropValue "ttt"
              Expect.isSome tttValue "'ttt' must be present in @context (ttt term IS used)"

              Expect.isTrue
                  (tttValue.Value.Contains "localhost/tictactoe#")
                  "ttt namespace must be the origin-resolved form containing 'localhost/tictactoe#'"

              Expect.isFalse
                  (body.Contains "example.org/tictactoe#")
                  "absolute placeholder 'example.org/tictactoe#' must NOT appear (discriminating)"
          }

          testCaseAsync "F-v2 schema prefix stays absolute when used"
          <| async {
              // schema is stored as an absolute URI 'https://schema.org/' and used in a body attr.
              // @context must map schema to exactly 'https://schema.org/'.
              let schemaOnlyConfig =
                  { defaultConfig with
                      DeclaredPrefixes = [ "schema", "https://schema.org/" ] }

              let builder = WebApplication.CreateBuilder()
              builder.WebHost.UseTestServer() |> ignore

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
                  :> IProvenanceStore

              builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(schemaOnlyConfig) |> ignore
              let app = builder.Build()
              let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

              let mkSchemaRecord id resource =
                  { mkRecord id resource with
                      BodyAttributes = [ "https://schema.org/actionStatus", Literal "Active" ] }

              resolvedStore.Append(mkSchemaRecord "urn:uuid:act-sc" "http://localhost/r")

              app.MapGet(
                  "/provenance",
                  Func<HttpContext, System.Threading.Tasks.Task>(
                      ProvenanceEndpoint.handle resolvedStore schemaOnlyConfig
                  )
              )
              |> ignore

              app.StartAsync().GetAwaiter().GetResult()
              use app = app
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              use doc = JsonDocument.Parse body
              let root = doc.RootElement

              let getCtxPropValue (name: string) =
                  let mutable ctxEl = Unchecked.defaultof<JsonElement>

                  if root.TryGetProperty("@context", &ctxEl) then
                      let tryGet (el: JsonElement) =
                          if el.ValueKind = JsonValueKind.Object then
                              let mutable propEl = Unchecked.defaultof<JsonElement>

                              if el.TryGetProperty(name, &propEl) then
                                  Some(propEl.GetString())
                              else
                                  None
                          else
                              None

                      match ctxEl.ValueKind with
                      | JsonValueKind.Object -> tryGet ctxEl
                      | JsonValueKind.Array -> ctxEl.EnumerateArray() |> Seq.tryPick tryGet
                      | _ -> None
                  else
                      None

              let schemaValue = getCtxPropValue "schema"
              Expect.isSome schemaValue "'schema' must be present in @context (schema term IS used)"

              Expect.equal
                  schemaValue.Value
                  "https://schema.org/"
                  "schema namespace must be exactly 'https://schema.org/' (absolute, no host-mangling)"
          }

          testCaseAsync "negative-k state entity node key returns 404 not 500"
          <| async {
              // RED before fix: handleStateEntity guards only k > records.Length.
              // A crafted key that decodes to k=-1 slips the guard and hits
              // buildStateEntityNodeGraph's invalidArg (k<0) → unhandled exception → 500.
              // GREEN after fix: k < 0 || k > records.Length → 404 before calling graph builder.
              let builder = WebApplication.CreateBuilder()
              builder.WebHost.UseTestServer() |> ignore

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
                  :> IProvenanceStore

              builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(defaultConfig) |> ignore
              let app = builder.Build()
              let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

              app.MapGet(
                  "/provenance/{nodeId}",
                  Func<HttpContext, System.Threading.Tasks.Task>(
                      ProvenanceEndpoint.handleNode resolvedStore defaultConfig
                  )
              )
              |> ignore

              app.StartAsync().GetAwaiter().GetResult()
              use app = app
              use client = app.GetTestClient()

              // Craft key encoding ("http://localhost/test", -1) using the same base64url
              // encoding as stateEntityIri — the only distinction is k=-1, which is negative.
              let negativeKKey =
                  let bytes = System.Text.Encoding.UTF8.GetBytes("http://localhost/test|-1")

                  System.Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')

              let! (resp: HttpResponseMessage) =
                  client.GetAsync(sprintf "/provenance/entity-%s" negativeKKey) |> Async.AwaitTask

              Expect.equal (int resp.StatusCode) 404 "crafted negative-k key must return 404 not 500"
          }

          testCaseAsync "#412 AC1: bare state-entity @context has prov only, http and rdfs absent"
          <| async {
              let resourceUri = "http://localhost/r"
              let records = [ mkRecord "http://localhost/provenance/act-1" resourceUri ]
              use app = startNodeServer records
              use client = app.GetTestClient()
              let fullIri = ProvenanceGraph.stateEntityIri "http://localhost" resourceUri 0
              let nodeId = fullIri.Substring(fullIri.LastIndexOf('/') + 1)

              let! (resp: HttpResponseMessage) = client.GetAsync(sprintf "/provenance/%s" nodeId) |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              let keys = ctxKeys body
              Expect.isTrue (keys.Contains "prov") "@context has 'prov'"
              Expect.isFalse (keys.Contains "http") "@context must NOT have 'http' — unused in bare state entity"
              Expect.isFalse (keys.Contains "rdfs") "@context must NOT have 'rdfs' — unused in bare state entity"
          }

          testCaseAsync "#412 AC2: activity node @context includes prov and http; rdfs absent without agent label"
          <| async {
              let record = mkRecord "http://localhost/provenance/act-1" "http://localhost/r"
              use app = startNodeServer [ record ]
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance/act-1") |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              let keys = ctxKeys body
              Expect.isTrue (keys.Contains "prov") "@context has 'prov'"
              Expect.isTrue (keys.Contains "http") "@context has 'http' (http:methodName/statusCodeValue used)"
              Expect.isFalse (keys.Contains "rdfs") "@context must NOT have 'rdfs' — no rdfs:label triple present"
          }

          testCaseAsync "#412 AC2b: activity node @context includes rdfs when agent has a label"
          <| async {
              let record =
                  { mkRecord "http://localhost/provenance/act-1" "http://localhost/r" with
                      Agent =
                          { Id = "urn:agent:alice"
                            Label = Some "alice" } }

              use app = startNodeServer [ record ]
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance/act-1") |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              let keys = ctxKeys body
              Expect.isTrue (keys.Contains "rdfs") "@context has 'rdfs' — rdfs:label triple present on agent"
          }

          testCaseAsync "#412 AC3: lineage batch @context is exactly the union of prefixes actually used"
          <| async {
              let record =
                  { mkRecord "http://localhost/provenance/act-1" "http://localhost/r" with
                      Agent =
                          { Id = "urn:agent:alice"
                            Label = Some "alice" } }

              use app = startNodeServer [ record ]
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              let keys = ctxKeys body

              Expect.equal
                  keys
                  (Set.ofList [ "prov"; "http"; "rdfs" ])
                  "@context is exactly {prov, http, rdfs} — all three used"
          }

          testCaseAsync "#412 AC3b: lineage batch @context omits rdfs when no agent has a label"
          <| async {
              let records = [ mkRecord "http://localhost/provenance/act-1" "http://localhost/r" ]
              use app = startNodeServer records
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) =
                  client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              let keys = ctxKeys body
              Expect.equal keys (Set.ofList [ "prov"; "http" ]) "@context is exactly {prov, http} — rdfs unused"
          } ]
