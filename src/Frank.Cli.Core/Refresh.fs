module Frank.Cli.Core.Refresh

open System
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher

type DriftEntry = { Prefix: string; Recorded: string; Current: string }
type RefreshReport = { Checked: int; Drifted: DriftEntry list }

let private checkOne (fetch: Fetch) (prefix: string) (entry: VocabularyEntry) : Async<Result<DriftEntry option, string>> =
    async {
        let! r = fetch (Uri entry.Uri)

        match r with
        | Error e -> return Error $"{prefix}: {e}"
        | Ok resp ->
            let h = sha256Hex resp.Body

            match detectDrift entry.Hash h with
            | NoDrift -> return Ok None
            | Drift(recorded, current) ->
                return Ok(Some { Prefix = prefix; Recorded = recorded; Current = current })
    }

let refresh (fetch: Fetch) (lf: LockFile) : Async<Result<RefreshReport, string>> =
    async {
        let entries = lf.Vocabularies |> Map.toList
        let mutable drifted: DriftEntry list = []
        let mutable errorResult: string option = None
        let mutable i = 0

        while i < entries.Length && errorResult.IsNone do
            let (prefix, entry) = entries.[i]
            let! check = checkOne fetch prefix entry

            match check with
            | Error e -> errorResult <- Some e
            | Ok None -> ()
            | Ok(Some d) -> drifted <- drifted @ [ d ]

            i <- i + 1

        return
            match errorResult with
            | Some e -> Error e
            | None -> Ok { Checked = Map.count lf.Vocabularies; Drifted = drifted }
    }
