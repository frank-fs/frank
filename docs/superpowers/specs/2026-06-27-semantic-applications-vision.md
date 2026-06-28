# Semantic Applications Vision — provenance + linked data as a feature substrate

Date: 2026-06-27
Status: VISION / roadmap (not a committed work item). Seeds future specs.
Builds on: the shipped v7.3.2 semantic verticals — Frank.Provenance, Frank.LinkedData, Frank.Discovery, Frank.Semantic.

## Thesis

The v7.3.2 semantic layer is not an end in itself. The provenance graph (who did what, when, to which resource, with what outcome) and the linked-data outbound references (what a resource IS, in terms a machine can follow) are a **substrate**: a single accumulated, queryable, self-describing record from which application features (analytics, leaderboards, head-to-head history) and agent capabilities (learn-the-game-by-following-links) are *derived queries*, not bespoke subsystems. This document captures the target features and shows each is reachable from data the framework already captures — proving why the semantic investment matters (the "built for agents" and "discovery first" pillars made concrete).

Worked example throughout: the TicTacToe-v732 sample.

## What the framework already captures

Each HTTP request that touches a resource produces a PROV-O record (`ProvenanceRecord` → compacted JSON-LD), persisted in the in-memory `MailboxProcessorStore` (bounded, evicting), queryable by resource and by agent:

| Field | PROV-O term | Carries |
|-------|-------------|---------|
| `ResourceUri` | `@id` of the `prov:Entity` | absolute IRI of the game/resource acted on |
| `Id` | `prov:Activity` | the action (a move) |
| `DomainType` | `@type` on the Activity | vocabulary IRI, e.g. `schema:MoveAction` (build-time-resolved) |
| `Agent.Id` | `prov:wasAssociatedWith` | the player IRI (`http://host/agents/<name>`) |
| `StartedAt` / `EndedAt` | `prov:startedAtTime` / `endedAtTime` | per-action timing |
| `HttpMethod` / `StatusCode` | `http:methodName` / `statusCodeValue` (W3C) | request shape + result |

Store surface today: `Append`, `QueryByResource`, `QueryByAgent` (async). LinkedData additionally emits, per resource, outbound `rdfs:seeAlso` / `owl:equivalentClass` links from the vocabulary CE (the game already `seeAlso`s `wikidata:Q210339`, tic-tac-toe).

## Target features, each mapped to captured data

### A. Leaderboard / top winners with weighted scoring (Premier-League style)
- **Need:** per player, count wins / losses / draws; apply multipliers (e.g. win=3, draw=1, loss=0; or any custom league weighting) → ranked table.
- **From captured data:** `QueryByAgent(player)` → that player's Activities. The *outcome* must be a typed Activity (currently only the move is typed `schema:MoveAction`). The lock ALREADY maps `MoveResult` cases (`Won → schema:CompletedActionStatus`, `Draw → schema:CompletedActionStatus`, `Error → schema:FailedActionStatus`, `XTurn/OTurn → schema:ActiveActionStatus`).
- **To build:** add `provClass`/`produces` typing on the terminal result so a win/draw is a distinguishable typed Activity; an aggregation query that buckets a player's terminal Activities by outcome type and applies a weighting function.
- **Gap size:** small — one vocabulary/handler addition + an aggregation query.

### B. Head-to-head (most frequent 1-on-1 opponents)
- **Need:** for a player, who have they faced most often, one-on-one.
- **From captured data:** each game resource has two agents `wasAssociatedWith` Activities on it. `QueryByResource(game)` → the agent pair; aggregate pairs across all games involving the player → frequency ranking.
- **To build:** an aggregation query (group Activities by game resource → extract agent pairs → count). No new capture.

### C. Move-timing analytics (slowest / fastest movers; longest gap between games)
- **Need:** players with longest/shortest move times; longest time between games.
- **From captured data:** `EndedAt − StartedAt` per Activity = move duration. `QueryByAgent(player)` ordered by time → gaps between consecutive game Activities = idle time.
- **To build:** aggregation/statistics queries over timestamps. No new capture.

### D. Concurrency (players running multiple games at once)
- **Need:** how many players have overlapping active games.
- **From captured data:** `QueryByAgent(player)` → Activities with `(game resource, time window)`; overlapping windows across *different* game resources → concurrent play.
- **To build:** an interval-overlap query per agent. No new capture.

### E. Agent learns the game from semantic relationships (the discovery thesis)
- **Need:** a lightly-prompted ("dumb") agent, given only the API entry point, discovers what the game is and how to play it well — ideally competitive with, or beating, a hand-prompted opponent.
- **From captured data:** GET a game → JSON-LD with `@type schema:Game` + `rdfs:seeAlso wikidata:Q210339` (tic-tac-toe). Follow-your-nose: the agent dereferences Wikidata/schema.org to learn the game's identity and rules. Discovery (ALPS/JSON Home) tells it the affordances (legal moves, transitions).
- **To build (for strategy):** add outbound `seeAlso` links in the vocabulary CE to **strategy** descriptions (e.g. Wikipedia "Tic-tac-toe#Strategy", a game-theory/solved-game reference). Tic-tac-toe is a *solved* game — optimal play is fully documented at the other end of those links. A naive agent that follows them can play perfectly; the experiment is whether discovery-only play matches/beats prompted play.
- **Gap size:** vocabulary additions (more `seeAlso`/`equivalentClass`), not new infrastructure. This is the headline demonstration of the "naive client via links" thesis (AGENT_HYPOTHESIS Track A).

## The gap (what is NOT built yet)

None of these require new architecture; all compose on shipped verticals.

1. **A query/aggregation layer over the provenance store.** Today the store exposes list lookups (`QueryByResource`/`QueryByAgent`). Features A–D are aggregations (group-by, count, interval-overlap, weighted-sum). Options: in-process F# aggregation over the queried lists; or expose the accumulated PROV-O as a graph and run SPARQL (dotNetRDF supports it) for declarative analytics. Decision deferred to a future spec.
2. **Outcome-typing on terminal moves** (feature A) — `provClass`/`produces` on the win/draw/error result.
3. **Strategy outbound links** (feature E) — vocabulary `seeAlso` additions.
4. **Durable store** (out of v7.3.2 scope) — analytics over history implies persistence beyond the bounded in-memory store; the current `IProvenanceStore` interface is the seam for a durable implementation.

## Why this validates the semantic investment

- Analytics features fall out of provenance **as queries**, not as hand-built tracking code — the framework records the right facts once, many features read them.
- The agent-learning demonstration is the project thesis made concrete: a resource that links outward (schema.org, Wikidata, strategy refs) lets an under-specified agent become competent by *following data*, not by prompt engineering.
- It connects all four semantic verticals into one story: Provenance (what happened) + LinkedData (what things are) + Discovery (what you can do) + Semantic (the shared vocabulary binding them).

## Next steps (when prioritized)

1. Spec the provenance query/aggregation layer (in-process vs SPARQL).
2. Outcome-typing + the leaderboard/weighted-scoring feature (smallest, highest-demo-value first).
3. Strategy outbound links + the naive-agent-vs-prompted-agent experiment.
4. Durable `IProvenanceStore` implementation when analytics-over-history is needed.

## Sources

- Shipped vertical: `docs/superpowers/specs/2026-06-27-v732-provenance-vertical-design.md`
- v7.3.2 semantic design: `docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md`
- Agent thesis tracks: `docs/AGENT_HYPOTHESIS.md`
- Wikidata tic-tac-toe: https://www.wikidata.org/wiki/Q210339
