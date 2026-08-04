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

See `sample/Frank.Validation.Sample` for a complete, runnable example.

## Non-goals

SHACL-JS, non-validating shape characteristics (`sh:name`/`sh:order`/...), and durable shape storage
are explicitly out of scope -- see `docs/superpowers/specs/2026-08-03-frank-validation-design.md`.
