namespace Frank.Semantic

open System
open System.IO
open System.Security.Cryptography
open VDS.RDF
open VDS.RDF.Parsing

module VocabFetcher =

    // ── Public types ──────────────────────────────────────────────────────────

    /// Supported RDF serialisation formats for vocabulary schemas.
    type VocabFormat =
        | JsonLd
        | RdfXml
        | Turtle

    /// Drift comparison result from detectDrift.
    type DriftResult =
        | NoDrift
        | Drift of recordedHash: string * currentHash: string

    /// Returned from a successful fetchAndCache call.
    type CachedVocab =
        { Hash: string
          Format: VocabFormat
          CacheFilePath: string
          Graph: IGraph }

    /// The fetch boundary: Uri → Async<Result<anonymous, reason>>.
    /// Inject a real HttpClient-backed implementation in production;
    /// inject a stub in tests to avoid network calls.
    type Fetch =
        Uri
            -> Async<
                Result<
                    {| ContentType: string option
                       Body: byte[] |},
                    string
                 >
             >

    // ── Pure helpers ──────────────────────────────────────────────────────────

    let private extensionMap =
        [ ".jsonld", JsonLd
          ".json", JsonLd
          ".rdf", RdfXml
          ".owl", RdfXml
          ".ttl", Turtle
          ".n3", Turtle ]
        |> Map.ofList

    let private contentTypeMap =
        [ "application/ld+json", JsonLd
          "application/rdf+xml", RdfXml
          "application/xml", RdfXml
          "text/turtle", Turtle
          "text/n3", Turtle ]
        |> Map.ofList

    let private stripParams (ct: string) =
        match ct.IndexOf(';') with
        | -1 -> ct.Trim().ToLowerInvariant()
        | idx -> ct.[.. idx - 1].Trim().ToLowerInvariant()

    /// Detect format from Content-Type header, falling back to URI file extension.
    /// Defaults to JsonLd when neither source resolves.
    let detectFormat (contentType: string option) (uri: Uri) : VocabFormat =
        let byContentType =
            contentType
            |> Option.bind (fun ct -> Map.tryFind (stripParams ct) contentTypeMap)

        match byContentType with
        | Some fmt -> fmt
        | None ->
            let ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant()
            Map.tryFind ext extensionMap |> Option.defaultValue JsonLd

    /// SHA-256 hex string (lowercase, 64 chars) of the given bytes.
    let sha256Hex (bytes: byte[]) : string =
        use sha = SHA256.Create()
        let hash = sha.ComputeHash(bytes)
        hash |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

    /// Canonical cache file name: <name>.<hash>.<ext>
    let cacheFileName (name: string) (hash: string) (format: VocabFormat) : string =
        let ext =
            match format with
            | JsonLd -> "jsonld"
            | RdfXml -> "rdf"
            | Turtle -> "ttl"

        $"{name}.{hash}.{ext}"

    // ── Parsing ───────────────────────────────────────────────────────────────

    let private parseJsonLd (bytes: byte[]) : Result<IGraph, string> =
        let graph = new Graph() :> IGraph
        let parser = JsonLdParser()

        try
            use store = new TripleStore()
            use stream = new MemoryStream(bytes)
            use reader = new StreamReader(stream)
            parser.Load(store :> ITripleStore, reader)

            store.Graphs |> Seq.iter (fun g -> graph.Merge(g))
            Ok graph
        with ex ->
            Error $"parse failed (JsonLd): {ex.Message}"

    let private parseWithIRdfReader (format: VocabFormat) (bytes: byte[]) : Result<IGraph, string> =
        let graph = new Graph() :> IGraph

        let parser: IRdfReader =
            match format with
            | RdfXml -> new RdfXmlParser() :> IRdfReader
            | Turtle -> new TurtleParser() :> IRdfReader
            | JsonLd -> failwith "parseWithIRdfReader: JsonLd must use parseJsonLd"

        try
            use stream = new MemoryStream(bytes)
            use reader = new StreamReader(stream)
            parser.Load(graph, reader)
            Ok graph
        with ex ->
            Error $"parse failed ({format}): {ex.Message}"

    /// Parse raw bytes into an IGraph. Returns Error with reason on failure.
    let parseGraph (format: VocabFormat) (bytes: byte[]) : Result<IGraph, string> =
        match format with
        | JsonLd -> parseJsonLd bytes
        | RdfXml -> parseWithIRdfReader RdfXml bytes
        | Turtle -> parseWithIRdfReader Turtle bytes

    // ── Cache helpers ─────────────────────────────────────────────────────────

    let private findCacheFile (cacheDir: string) (name: string) : string option =
        let pattern = $"{name}.*"
        let files = Directory.GetFiles(cacheDir, pattern)

        match files with
        | [||] -> None
        | _ -> Some files.[0]

    let private loadCacheFile (filePath: string) : Result<byte[] * VocabFormat, string> =
        let ext = Path.GetExtension(filePath).ToLowerInvariant()

        let formatResult =
            match ext with
            | ".jsonld" -> Ok JsonLd
            | ".rdf" -> Ok RdfXml
            | ".ttl" -> Ok Turtle
            | unknown -> Error $"unrecognised cached extension '{unknown}'"

        match formatResult with
        | Error e -> Error e
        | Ok fmt ->
            try
                Ok(File.ReadAllBytes filePath, fmt)
            with ex ->
                Error $"could not read cache file: {ex.Message}"

    // ── Effectful: fetch and cache ────────────────────────────────────────────

    /// Fetch a vocabulary URI, parse it, and write it to cacheDir.
    /// Returns CachedVocab on success.
    /// Cache hit (file matching <name>.*) returns cached result without invoking fetch.
    /// fetch failure returns Error with reason; cache dir is left untouched.
    let fetchAndCache (fetch: Fetch) (cacheDir: string) (name: string) (uri: Uri) : Async<Result<CachedVocab, string>> =
        if String.IsNullOrWhiteSpace name then
            invalidArg (nameof name) "name must not be empty"

        async {
            match findCacheFile cacheDir name with
            | Some filePath ->
                match loadCacheFile filePath with
                | Error e -> return Error e
                | Ok(bytes, format) ->
                    match parseGraph format bytes with
                    | Error e -> return Error e
                    | Ok graph ->
                        return
                            Ok
                                { Hash = sha256Hex bytes
                                  Format = format
                                  CacheFilePath = filePath
                                  Graph = graph }
            | None ->
                let! fetchResult = fetch uri

                match fetchResult with
                | Error reason -> return Error reason
                | Ok response ->
                    let format = detectFormat response.ContentType uri

                    match parseGraph format response.Body with
                    | Error e -> return Error e
                    | Ok graph ->
                        let hash = sha256Hex response.Body
                        let fileName = cacheFileName name hash format
                        let filePath = Path.Combine(cacheDir, fileName)

                        try
                            File.WriteAllBytes(filePath, response.Body)

                            return
                                Ok
                                    { Hash = hash
                                      Format = format
                                      CacheFilePath = filePath
                                      Graph = graph }
                        with ex ->
                            return Error $"could not write cache file: {ex.Message}"
        }

    // ── Pure: detect drift ────────────────────────────────────────────────────

    /// Compare recorded hash to current hash.
    /// Returns NoDrift or Drift(recorded, current).
    /// B3 only compares — it does not mutate any mappings.
    let detectDrift (recordedHash: string) (currentHash: string) : DriftResult =
        if recordedHash = currentHash then
            NoDrift
        else
            Drift(recordedHash, currentHash)

    // ── Production boundary ───────────────────────────────────────────────────

    /// Production Fetch implementation backed by a shared HttpClient.
    let httpFetch (client: Net.Http.HttpClient) : Fetch =
        fun uri ->
            async {
                try
                    let! response = client.GetAsync(uri) |> Async.AwaitTask

                    if not response.IsSuccessStatusCode then
                        return Error $"HTTP {int response.StatusCode} for {uri}"
                    else
                        let contentType =
                            response.Content.Headers.ContentType
                            |> Option.ofObj
                            |> Option.map (fun ct -> ct.MediaType)

                        let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask

                        return
                            Ok
                                {| ContentType = contentType
                                   Body = bytes |}
                with ex ->
                    return Error ex.Message
            }
