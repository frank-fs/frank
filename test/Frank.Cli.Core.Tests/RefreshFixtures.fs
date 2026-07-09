module Frank.Cli.Core.Tests.RefreshFixtures

open System
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher

let schemaBody: byte[] =
    Text.Encoding.UTF8.GetBytes "@prefix schema: <https://schema.org/> .\nschema:Game a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

let schemaBodyHash: string = sha256Hex schemaBody

// ── ConnegFetch stubs ─────────────────────────────────────────────────────────

/// Turtle ConnegFetchResult for the given body bytes.
let turtleResult (body: byte[]) : ConnegFetchResult =
    RdfContent
        {| MediaType = "text/turtle"
           Body = body
           HttpStatus = 200
           ETag = None
           LastModified = None
           CacheControlMaxAge = None |}

/// Returns a ConnegFetch that always returns the given result.
let stubConnegFetch (result: ConnegFetchResult) : ConnegFetch =
    fun _uri _etag _lastMod -> async { return result }

/// Returns a ConnegFetch serving Turtle body; and a counter ref tracking request count.
let countingConnegFetch (result: ConnegFetchResult) : ConnegFetch * int ref =
    let count = ref 0

    let fetch : ConnegFetch =
        fun _uri _etag _lastMod ->
            incr count
            async { return result }

    fetch, count

/// Returns a ConnegFetch that always returns a Turtle result with the given body.
let stubTurtleConnegFetch (body: byte[]) : ConnegFetch = stubConnegFetch (turtleResult body)

/// A ConnegFetch that always fails with the given HTTP status.
let stubHttpError (status: int) (uri: Uri) : ConnegFetch =
    fun _uri _etag _lastMod -> async { return HttpErrorStatus(status, uri) }

// ── VocabularyEntry helpers ───────────────────────────────────────────────────

let mkVocabEntry (hash: string) : VocabularyEntry =
    { v1Empty with
        Uri = "https://schema.org/"
        FetchedAt = DateTimeOffset.UnixEpoch
        Hash = hash }

let mkOwnedEntry (uri: string) (fetchedDaysAgo: float) : VocabularyEntry =
    { v1Empty with
        Uri = uri
        FetchedAt = DateTimeOffset.UtcNow.AddDays(-fetchedDaysAgo)
        Hash = sha256Hex schemaBody
        Owned = true }

let mkUnownedEntry (uri: string) (fetchedDaysAgo: float) : VocabularyEntry =
    { v1Empty with
        Uri = uri
        FetchedAt = DateTimeOffset.UtcNow.AddDays(-fetchedDaysAgo)
        Hash = sha256Hex schemaBody
        Owned = false }
