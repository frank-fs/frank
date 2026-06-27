module Frank.Validation.JsonLdLoader

open System
open System.Collections.Generic
open Newtonsoft.Json.Linq
open VDS.RDF.JsonLd

/// Offline JSON-LD context loader. For a context IRI that matches a known vocabulary
/// namespace, returns a synthesized {"@context":{"@vocab": ns}} document so bare terms
/// expand by concatenation to the SAME IRIs Frank's shapes use. Fails closed (throws)
/// for any unknown context IRI — a validator must never let missing context look like conforming data.
let synthesizing (namespaces: string seq) : JsonLdDocumentLoader =
    let index = Dictionary<string, string>(StringComparer.Ordinal)

    for ns in namespaces do
        let json = sprintf """{"@context":{"@vocab":"%s"}}""" ns
        index.[ns] <- json
        index.[ns.TrimEnd('/')] <- json

    let load (uri: Uri) (_opts: JsonLdLoaderOptions) : RemoteDocument =
        let key = uri.AbsoluteUri

        match index.TryGetValue(key) with
        | true, json ->
            let doc = RemoteDocument()
            doc.DocumentUrl <- uri
            doc.Document <- JObject.Parse(json)
            doc
        | false, _ ->
            failwithf
                "Frank.Validation: no known vocabulary namespace for JSON-LD @context '%s'; \
                 declare its prefix in the vocabulary CE"
                key

    JsonLdDocumentLoader(load)
