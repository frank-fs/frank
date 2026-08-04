# Frank.Validation

Hand-authored SHACL Core (+ SPARQL-based constraints, + the full property-path grammar) validation
for Frank resources, built on [Frank.Rdf](../Frank.Rdf/README.md).

## Authoring a shape

```fsharp
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

let personShape =
    shape (targetClass (Uri "https://schema.org/Person")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/email")) {
                datatype XsdDatatype.String
                pattern @"^\S+@\S+\.\S+$"
                minCount 1
            }
        ]
        closed []
    }
```

`shape { }`/`property { }` are optional sugar over `ShapeSpecFunctions` -- both produce identical
`ShapeDecl`/`PropertyShapeSpec` values; use whichever reads better at the call site.

## Validating a graph

```fsharp
let shapesGraph = Shacl.toShapesGraph [ personShape ]

match Shacl.validate shapesGraph someDataGraph with
| ValidationOutcome.Conforms -> ()
| ValidationOutcome.Violates violations -> (* ... *)
```

## Validating HTTP request bodies

```fsharp
resource "/people" {
    useValidation shapesGraph
    post createPerson
}

webHost args {
    useDefaults
    useValidation   // registers the one app-wide interceptor -- required once, app-wide
    resource peopleResource
}
```

`POST`/`PUT`/`PATCH` requests with `Content-Type: application/ld+json` to a `useValidation`-declared
resource are buffered, parsed, and validated before the handler runs. A conforming request continues
to the handler unchanged; a violating request gets 422, content-negotiated between a real
`sh:ValidationReport` (`Accept: application/ld+json`) and `application/problem+json` (everything else).

### What is, and is not, intercepted

A request is validated only when **all** of the following hold. Anything else passes straight
through to the handler, unvalidated:

| | Intercepted |
|---|---|
| Resource | declared `useValidation shapesGraph` on its `resource { }` |
| App | called `useValidation` once on its `webHost { }` (without this, *nothing* is validated) |
| Method | `POST`, `PUT` or `PATCH` — never `GET`/`DELETE`/`HEAD`/`OPTIONS` |
| `Content-Type` | starts with `application/ld+json` (case-insensitive), so `application/ld+json; charset=utf-8` counts |

> **The `Content-Type` check is a real bypass, not a formality.** A client that sends the very same
> body as `application/json`, or with no `Content-Type` header at all, to a `useValidation`-declared
> resource reaches your handler **unvalidated**. This is deliberate — the middleware only ever
> narrows JSON-LD, and it must not reinterpret a payload whose media type says it is something else —
> but it means `useValidation` is *not* a guarantee that every request your handler sees has been
> SHACL-checked.
>
> A handler that must not run on unvalidated input is responsible for its own defence: reject
> unexpected content types itself (a `415 Unsupported Media Type` is the usual answer), or treat
> `Validation.tryGetValidatedGraph ctx` returning `None` as "this body was never validated" and act
> accordingly.

Other short-circuit responses, all `application/problem+json`:

| Condition | Status |
|---|---|
| Body over the 1 MiB buffering limit | `413` |
| Body is not parseable JSON-LD | `400` (a parse failure is not a SHACL violation) |
| SHACL violation | `422` (or a `sh:ValidationReport` under `Accept: application/ld+json`) |
| Unexpected failure inside the RDF/SHACL layer | `500`, logged |

### Reading the parsed graph in a handler

A conforming request's parsed graph is stashed for the handler, so it never has to parse the body a
second time:

```fsharp
let postMove =
    fun (ctx: HttpContext) ->
        task {
            match Validation.tryGetValidatedGraph ctx with
            | Some graph -> // the graph the middleware already validated
            | None -> // this request was not validated -- see the bypass note above
        }
```

An empty body parses to an empty graph and conforms trivially, unless a shape targets via
`TargetSpec.Node`.

### A note on SPARQL constraints

`sh:sparql` takes a SPARQL **SELECT** query, and every row it returns is one violation — so write the
query to select what is *wrong*. `Shacl.toShapesGraph` parses each query at shape-build time and
raises there if it does not parse, or is not a SELECT, rather than letting a shape bug fail every
request to the resource it guards. An `ASK { P }` inverts to `SELECT $this WHERE { FILTER NOT EXISTS { P } }`.

See `sample/Frank.Validation.Sample` for a complete, runnable example.

## Non-goals

SHACL-JS, non-validating shape characteristics (`sh:name`/`sh:order`/...), and durable shape storage
are explicitly out of scope -- see `docs/superpowers/specs/2026-08-03-frank-validation-design.md`.
