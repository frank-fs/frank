module TicTacToe.GameStore

open System.Collections.Generic
open TicTacToe.Model

/// Request/response messages for the store actor. No IObservable — the
/// naive REST client polls; SSE/observable belongs to Track B/C.
type private Msg =
    | GetOrCreate of string * AsyncReplyChannel<MoveResult>
    | Get of string * AsyncReplyChannel<MoveResult option>
    | Update of string * Move * AsyncReplyChannel<MoveResult option>

/// In-memory game store keyed by caller-supplied id. State lives inside the
/// actor (no module-level mutable). Derived from TicTacToe.Web.Simple.GameStore.
type GameStore() =
    let agent =
        MailboxProcessor<Msg>.Start(fun inbox ->
            let games = Dictionary<string, MoveResult>()

            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | GetOrCreate(id, reply) ->
                        match games.TryGetValue id with
                        | true, r -> reply.Reply r
                        | false, _ ->
                            let r = startGame ()
                            games.[id] <- r
                            reply.Reply r

                        return! loop ()

                    | Get(id, reply) ->
                        match games.TryGetValue id with
                        | true, r -> reply.Reply(Some r)
                        | false, _ -> reply.Reply None

                        return! loop ()

                    | Update(id, move, reply) ->
                        match games.TryGetValue id with
                        | true, current ->
                            let next = makeMove (current, move)
                            // Error keeps the current state; do not persist it.
                            match next with
                            | Error _ -> reply.Reply(Some next)
                            | _ ->
                                games.[id] <- next
                                reply.Reply(Some next)
                        | false, _ -> reply.Reply None

                        return! loop ()
                }

            loop ())

    member _.GetOrCreate(id: string) : MoveResult =
        agent.PostAndReply(fun ch -> GetOrCreate(id, ch))

    member _.Get(id: string) : MoveResult option =
        agent.PostAndReply(fun ch -> Get(id, ch))

    member _.Update(id: string, move: Move) : MoveResult option =
        agent.PostAndReply(fun ch -> Update(id, move, ch))
