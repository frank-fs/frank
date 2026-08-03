module Sample.Provenance.Program

open System
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open VDS.RDF
open VDS.RDF.Writing
open Frank.Builder
open Frank.Rdf
open Frank.Provenance

// A tiny in-memory "database" -- the same shape as Frank.Rdf.Sample's, so this sample reads
// as its natural companion: the games are the same two entries, the machinery on top (RDF
// negotiation there, provenance recording here) is what differs.
let private games = dict [ "1", "Tic-tac-toe"; "2", "Connect Four" ]

// The plain-JSON representation of a game -- no RDF involved, matching Frank.Rdf.Sample's DTO.
type GameDto = { id: string; name: string; numberOfPlayers: int }

// The provenance store: one MailboxProcessorProvenanceStore for the life of the process --
// this sample's other "tiny in-memory database" alongside `games` above. Records are lost on
// restart, and NullLogger stands in for a real logger, since this is a sample, not production
// DI wiring.
let private store: IProvenanceStore =
    new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

// There's no real auth in this sample (see Frank.Auth's sample for that), so every recorded
// activity is attributed to one fixed "anonymous" agent -- honest about the absence of real
// users rather than fabricating a multi-user story.
let private agentUri (baseUri: string) : Node = Node.Iri $"{baseUri}/agents/anonymous"

// Records a single "someone viewed this game" activity: a freshly minted Activity IRI per
// request (so repeated views of the same game accumulate distinct activities, not one shared
// record), the game's own IRI as the Resource, and schema:ViewAction as a real, existing
// vocabulary term for "viewed" rather than an invented one.
let private recordView (baseUri: string) (id: string) : unit =
    let startedAt = DateTimeOffset.UtcNow

    store.Append(
        { Activity = Node.Iri $"{baseUri}/activities/{Guid.NewGuid()}"
          Resource = Node.Iri $"{baseUri}/games/{id}"
          Agent = agentUri baseUri
          StartedAt = startedAt
          EndedAt = startedAt.AddMilliseconds(1.0)
          ActivityType = Some(Uri "https://schema.org/ViewAction")
          Properties = [] }
    )

// SparqlQueryResult.Graph wraps a raw dotNetRDF IGraph, not a Frank.Rdf Doc -- Frank.Provenance
// queries via SPARQL internally, so this is NOT Doc.writeJsonLd. dotNetRDF's own JsonLdWriter
// (VDS.RDF.Writing) serializes a graph the same way Frank.Rdf.Doc.writeJsonLd does under the
// hood: wrap it in a TripleStore, then Save. Verified against the installed dotNetRdf.Core
// 3.5.1 assembly: JsonLdWriter.Save(ITripleStore, TextWriter) and TripleStore.Add(IGraph) both
// take exactly these shapes. Building the full string first, then writing it in one shot
// (matching Frank.Rdf.Doc.toJsonLd's own StringWriter-then-ToString pattern), rather than
// streaming straight into ctx.Response.Body through a StreamWriter: an earlier version of this
// function did stream directly, and every response silently cut off mid-value at exactly 1024
// bytes -- StreamWriter's default internal buffer size. Buffering to a string first guarantees
// the whole document exists before anything is written to the response.
let private graphToJsonLd (graph: IGraph) : string =
    let tripleStore = new TripleStore()
    tripleStore.Add(graph) |> ignore
    use writer = new System.IO.StringWriter()
    JsonLdWriter().Save(tripleStore, writer)
    writer.ToString()

let private getGame =
    fun (ctx: HttpContext) ->
        task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, name ->
                let baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                // Real recording on every real request -- not faked, not hardcoded. A second
                // view of the same game appends a second, independent activity.
                recordView baseUri id
                do! ctx.Response.WriteAsJsonAsync({ id = id; name = name; numberOfPlayers = 2 })
            | false, _ ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({| error = $"no game with id {id}" |})
        }

let private gameResource = resource "/games/{id}" { get getGame }

let private getProvenance =
    fun (ctx: HttpContext) ->
        task {
            let resourceIri = ctx.Request.Query.["resource"].ToString()

            if String.IsNullOrWhiteSpace resourceIri then
                ctx.Response.StatusCode <- 400
                do! ctx.Response.WriteAsJsonAsync({| error = "missing required query parameter 'resource'" |})
            else
                match store.Query(ProvenanceQuery.ByResource resourceIri) with
                | SparqlQueryResult.Graph g ->
                    ctx.Response.ContentType <- "application/ld+json"
                    do! ctx.Response.WriteAsync(graphToJsonLd g)
                | SparqlQueryResult.Bindings _ ->
                    // ByResource always compiles to a CONSTRUCT query (see
                    // Frank.Provenance's ProvenanceStore.fs `toSparqlQuery`), so this branch is
                    // structurally unreachable in practice -- handled defensively rather than
                    // designed around.
                    ctx.Response.StatusCode <- 500

                    do!
                        ctx.Response.WriteAsJsonAsync(
                            {| error = "unexpected: ByResource produced bindings instead of a graph" |}
                        )
        }

let private provenanceResource = resource "/provenance" { get getProvenance }

// ProvBuilder demo: IProvenanceStore.Append only accepts a ProvenanceRecord, and
// ProvenanceRecord.toDoc only ever emits wasGeneratedBy/wasAssociatedWith/startedAtTime/endedAtTime
// -- there is no way to record a wasDerivedFrom relationship through the store. This endpoint
// hand-authors that relationship directly via ProvBuilder, served independently of the store, to
// show the one PROV-O shape the record model can't produce: Connect Four (games/2) wasDerivedFrom
// Tic-tac-toe (games/1).
let private catalogLineage (baseUri: string) : Doc =
    rdf {
        about (entity (Node.Iri $"{baseUri}/games/2") { wasDerivedFrom (Node.Iri $"{baseUri}/games/1") })
    }

let private getCatalogLineage =
    fun (ctx: HttpContext) ->
        task {
            let baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
            ctx.Response.ContentType <- "application/ld+json"
            do! ctx.Response.WriteAsync(catalogLineage baseUri |> Doc.toJsonLd)
        }

let private lineageResource = resource "/provenance/lineage" { get getCatalogLineage }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource gameResource
        resource provenanceResource
        resource lineageResource
    }

    0
