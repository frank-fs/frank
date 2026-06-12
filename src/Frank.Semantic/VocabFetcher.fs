namespace Frank.Semantic

open System
open System.IO
open System.Net.Http
open System.Security.Cryptography
open VDS.RDF
open VDS.RDF.JsonLd
open VDS.RDF.JsonLd.Syntax
open VDS.RDF.Parsing

// ── Public types ─────────────────────────────────────────────────────────────

type FetchResult =
    { Prefix: string
      Uri: string
      Graph: IGraph
      Hash: string
      CachedPath: string }

// ── Implementation ────────────────────────────────────────────────────────────

module VocabFetcher =

    let private computeHash (bytes: byte[]) : string =
        let hash = SHA256.HashData(bytes)
        "sha256:" + Convert.ToHexString(hash).ToLowerInvariant()

    let private parseGraph (bytes: byte[]) (ext: string) : IGraph =
        let graph = new Graph()
        use stream = new MemoryStream(bytes)
        use reader = new StreamReader(stream)

        match ext with
        | ".ttl" ->
            let parser = TurtleParser()
            parser.Load(graph, reader)
        | ".rdf"
        | ".xml" ->
            let parser = RdfXmlParser()
            parser.Load(graph, reader)
        | _ ->
            let opts = JsonLdProcessorOptions(ProcessingMode = JsonLdProcessingMode.JsonLd11)
            use store = new TripleStore()
            let parser = JsonLdParser(opts)
            use jsonReader = new StringReader(System.Text.Encoding.UTF8.GetString(bytes))
            parser.Load(store, jsonReader)

            for g in store.Graphs do
                graph.Merge(g)

        graph

    let private findCached (cacheDir: string) (prefix: string) : (string * string) option =
        if not (Directory.Exists cacheDir) then
            None
        else
            let patterns = [| "*.jsonld"; "*.ttl"; "*.rdf"; "*.xml" |]

            patterns
            |> Array.tryPick (fun pat ->
                Directory.GetFiles(cacheDir, $"{prefix}.{pat}")
                |> Array.tryHead
                |> Option.map (fun path ->
                    let ext = Path.GetExtension(path)
                    path, ext))

    let private extensionFromContentType (contentType: string) : string =
        let ct = contentType.ToLowerInvariant()

        if ct.Contains("ld+json") || ct.Contains("application/json") then
            ".jsonld"
        elif
            ct.Contains("rdf+xml")
            || ct.Contains("application/xml")
            || ct.Contains("text/xml")
        then
            ".rdf"
        elif ct.Contains("turtle") || ct.Contains("text/plain") then
            ".ttl"
        else
            ".jsonld"

    let private extensionFromUri (uri: string) : string =
        let lower = uri.ToLowerInvariant()

        if lower.EndsWith(".ttl") then
            ".ttl"
        elif lower.EndsWith(".rdf") || lower.EndsWith(".xml") then
            ".rdf"
        else
            ".jsonld"

    let private fetchBytes (uri: string) : (byte[] * string) =
        use client = new HttpClient()
        client.DefaultRequestHeaders.Add("Accept", "application/ld+json, application/rdf+xml, text/turtle, */*")

        let response =
            try
                client.GetAsync(uri).GetAwaiter().GetResult()
            with ex ->
                raise (InvalidOperationException($"Failed to fetch vocabulary from '{uri}': {ex.Message}", ex))

        if not response.IsSuccessStatusCode then
            raise (
                InvalidOperationException(
                    $"Failed to fetch vocabulary from '{uri}': HTTP {int response.StatusCode} {response.ReasonPhrase}"
                )
            )

        let bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()

        let contentType =
            match response.Content.Headers.ContentType with
            | null -> ""
            | ct -> ct.MediaType

        let ext =
            if not (String.IsNullOrEmpty contentType) then
                extensionFromContentType contentType
            else
                extensionFromUri uri

        bytes, ext

    /// Fetch or load from cache. Returns FetchResult.
    /// Cache key: any file matching <prefix>.*.(jsonld|ttl|rdf|xml) in cacheDir.
    let fetchOrLoad (cacheDir: string) (prefix: string) (uri: string) : FetchResult =
        match findCached cacheDir prefix with
        | Some(cachedPath, ext) ->
            let bytes = File.ReadAllBytes(cachedPath)
            let hash = computeHash bytes
            let graph = parseGraph bytes ext

            { Prefix = prefix
              Uri = uri
              Graph = graph
              Hash = hash
              CachedPath = cachedPath }

        | None ->
            let bytes, ext = fetchBytes uri
            let hash = computeHash bytes
            let hashHex = hash.Substring(7)

            if not (Directory.Exists cacheDir) then
                Directory.CreateDirectory(cacheDir) |> ignore

            let cachedPath = Path.Combine(cacheDir, $"{prefix}.{hashHex}{ext}")
            File.WriteAllBytes(cachedPath, bytes)

            let graph = parseGraph bytes ext

            { Prefix = prefix
              Uri = uri
              Graph = graph
              Hash = hash
              CachedPath = cachedPath }

    /// Re-fetch each vocabulary listed in the lock file; compare against recorded hashes.
    /// Returns list of (prefix, oldHash, newHash) for any drifted vocabulary.
    /// Does NOT mutate the lock file.
    let detectDrift (cacheDir: string) (lockFile: LockFile) : (string * string * string) list =
        [ for KeyValue(prefix, entry) in lockFile.Vocabularies do
              match entry.Hash with
              | None -> ()
              | Some oldHash ->
                  match findCached cacheDir prefix with
                  | None -> ()
                  | Some(cachedPath, _) ->
                      let bytes = File.ReadAllBytes(cachedPath)
                      let currentHash = computeHash bytes

                      if currentHash <> oldHash then
                          yield prefix, oldHash, currentHash ]
