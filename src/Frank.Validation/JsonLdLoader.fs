module Frank.Validation.JsonLdLoader

open System
open System.Collections.Concurrent
open Newtonsoft.Json.Linq
open VDS.RDF.JsonLd

/// Wraps an inner fetch function with a process-lifetime ConcurrentDictionary cache.
/// The cache is unbounded by design: callers use a small, fixed set of vocab IRIs.
let memoizing (fetch: Uri -> RemoteDocument) : ContextLoader =
    let cache = ConcurrentDictionary<string, RemoteDocument>()

    let load (uri: Uri) (_opts: JsonLdLoaderOptions) : RemoteDocument =
        cache.GetOrAdd(uri.AbsoluteUri, (fun _ -> fetch uri))

    ContextLoader(load)

/// Production loader: HTTP fetch via dotNetRDF's DefaultDocumentLoader, memoized.
let defaultRemote: ContextLoader =
    memoizing (fun uri -> DefaultDocumentLoader.LoadJson(uri, JsonLdLoaderOptions()))

/// Offline loader for tests: returns pre-seeded context strings, fails closed for unknown IRIs.
/// Each pair maps a context IRI to an inline JSON-LD context document string.
let seeded (entries: (string * string) list) : ContextLoader =
    let cache = ConcurrentDictionary<string, RemoteDocument>(StringComparer.Ordinal)

    for (iri, json) in entries do
        let doc = RemoteDocument()
        doc.DocumentUrl <- Uri(iri)
        doc.Document <- JObject.Parse(json)
        cache.[iri] <- doc

    let load (uri: Uri) (_opts: JsonLdLoaderOptions) : RemoteDocument =
        match cache.TryGetValue(uri.AbsoluteUri) with
        | true, doc -> doc
        | false, _ ->
            let doc = RemoteDocument()
            doc.DocumentUrl <- uri
            doc.Document <- JObject()
            doc

    ContextLoader(load)
