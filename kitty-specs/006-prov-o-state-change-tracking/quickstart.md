# Quickstart: Frank.Provenance

**Date**: 2026-03-07
**Feature**: 006-prov-o-state-change-tracking

## 1. Add Frank.Provenance to Your Project

```bash
dotnet add package Frank.Provenance
```

Or add a project reference if building from source:

```xml
<ProjectReference Include="../Frank.Provenance/Frank.Provenance.fsproj" />
```

Frank.Provenance depends on Frank, Frank.LinkedData, and Frank.Statecharts. These must also be referenced.

## 2. Enable Provenance with `useProvenance`

Add `useProvenance` to your `webHost` computation expression alongside `useStatecharts`:

```fsharp
open Frank.Builder
open Frank.Statecharts
open Frank.Provenance

let app = webHost {
    useStatecharts
    useProvenance

    resource "/orders/{orderId}" {
        statefulResource orderStateMachine {
            inState Pending {
                post handleSubmitOrder
            }
            inState Submitted {
                post handleFulfillOrder
                delete handleCancelOrder
            }
            inState Fulfilled {
                // terminal state, no mutations
            }
        }
    }
}
```

Every successful state transition on the `statefulResource` automatically produces a PROV-O provenance record. No per-resource configuration is needed.

### Custom Retention Limit

Override the default 10,000-record limit:

```fsharp
let app = webHost {
    useStatecharts
    useProvenance { maxRecords 50_000 }
    // ...
}
```

## 3. Query Provenance via Accept Header

Provenance is served on the same resource URI using custom `Accept` media types. No separate endpoint is needed.

### Turtle

```bash
curl -H "Accept: application/vnd.frank.provenance+turtle" \
     http://localhost:5000/orders/42
```

Response:

```turtle
@prefix prov: <http://www.w3.org/ns/prov#> .
@prefix frank: <https://frank-web.dev/ns/provenance/> .

<activity/a1b2c3>  a  prov:Activity ;
    prov:startedAtTime  "2026-03-07T14:30:00Z"^^xsd:dateTime ;
    prov:wasAssociatedWith  <agent/jane.doe> ;
    prov:used  <entity/e1> ;
    frank:httpMethod  "POST" ;
    frank:eventName   "SubmitOrder" .

<agent/jane.doe>  a  prov:Person ;
    prov:label  "Jane Doe" .

<entity/e1>  a  prov:Entity ;
    frank:stateName  "Pending" .

<entity/e2>  a  prov:Entity ;
    prov:wasGeneratedBy  <activity/a1b2c3> ;
    prov:wasDerivedFrom  <entity/e1> ;
    frank:stateName  "Submitted" .
```

### JSON-LD

```bash
curl -H "Accept: application/vnd.frank.provenance+ld+json" \
     http://localhost:5000/orders/42
```

### RDF/XML

```bash
curl -H "Accept: application/vnd.frank.provenance+rdf+xml" \
     http://localhost:5000/orders/42
```

### Normal Resource Response (Unchanged)

Standard `Accept` headers return the normal resource representation, not provenance:

```bash
curl -H "Accept: application/json" http://localhost:5000/orders/42
# -> normal order JSON, no provenance
```

## 4. Agent Type Discrimination

Provenance automatically classifies agents based on request context:

| Request Context | Agent Type | PROV-O Class |
|----------------|------------|-------------|
| Authenticated user (`ClaimsPrincipal` with identity) | `prov:Person` | Name + identifier from claims |
| Unauthenticated request | `prov:SoftwareAgent` | System identifier |
| `X-Agent-Type: llm` header | `prov:SoftwareAgent` + `frank:LlmAgent` | Identifier + optional model from `X-Agent-Model` |

No configuration needed -- agent classification is automatic.

## 5. Custom Provenance Store (Advanced)

Replace the default in-memory store by implementing `IProvenanceStore` and registering it in DI:

```fsharp
type MyTripleStore(connectionString: string) =
    interface IProvenanceStore with
        member _.Append(record) = // write to external triple store
        member _.QueryByResource(uri) = // SPARQL query
        member _.QueryByAgent(agentId) = // SPARQL query
        member _.QueryByTimeRange(start, end_) = // SPARQL query
    interface IDisposable with
        member _.Dispose() = // cleanup

let app = webHost {
    useStatecharts
    useProvenance
    services (fun svc ->
        svc.AddSingleton<IProvenanceStore>(fun sp ->
            MyTripleStore("bolt://localhost:7687") :> IProvenanceStore)
        |> ignore
    )
    // ...
}
```

When a custom `IProvenanceStore` is registered in DI, the default `MailboxProcessorProvenanceStore` is not created.
