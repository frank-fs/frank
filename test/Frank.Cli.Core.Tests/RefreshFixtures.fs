module Frank.Cli.Core.Tests.RefreshFixtures

open System
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher

let schemaBody: byte[] =
    Text.Encoding.UTF8.GetBytes "{ \"@context\": \"https://schema.org/\" }"

let schemaBodyHash: string = sha256Hex schemaBody

let stubFetch (body: byte[]) : Fetch =
    fun (_: Uri) -> async { return Ok {| ContentType = None; Body = body |} }

let mkVocabEntry (hash: string) : VocabularyEntry =
    { v1Empty with
        Uri = "https://schema.org/"
        FetchedAt = DateTimeOffset.UnixEpoch
        Hash = hash }
