# Quickstart: Semantic Resources Phase 1

**Date**: 2026-03-04
**Feature**: 001-semantic-resources-phase1

## Developer Workflow

### 1. Install frank-cli

```bash
dotnet tool install frank-cli
```

### 2. Add MSBuild integration to your project

```bash
dotnet add package Frank.Cli.MSBuild
```

### 3. Build your project (required precondition)

```bash
dotnet build
```

### 4. Extract ontology from your Frank project

```bash
frank-cli extract --project MyApp.fsproj
```

Optional parameters:
- `--base-uri https://example.com/ontology/` — override default namespace
- `--vocabularies schema.org,hydra` — specify standard vocabularies (default)
- `--scope project|file|resource` — extraction scope

### 5. Review and refine

```bash
# Surface ambiguities
frank-cli clarify --project MyApp.fsproj

# Re-extract with clarifications resolved
frank-cli extract --project MyApp.fsproj

# Check completeness
frank-cli validate --project MyApp.fsproj

# Review changes from previous extraction
frank-cli diff --project MyApp.fsproj
```

### 6. Compile semantic definitions

```bash
frank-cli compile --project MyApp.fsproj
```

This generates OWL/XML and SHACL artifacts in `obj/frank-cli/`. The MSBuild targets from `Frank.Cli.MSBuild` automatically embed them on next `dotnet build`.

### 7. Rebuild to embed

```bash
dotnet build
```

### 8. Add LinkedData to your resource

```fsharp
open Frank.LinkedData

let products = resource "/products/{id}" {
    linkedData
    get (fun ctx -> task {
        let id = ctx.Request.RouteValues["id"] :?> string
        let product = getProduct id
        return! ctx.Negotiate(200, product)
    })
}
```

### 9. Test semantic content negotiation

```bash
# JSON-LD
curl -H "Accept: application/ld+json" http://localhost:5000/products/42

# Turtle
curl -H "Accept: text/turtle" http://localhost:5000/products/42

# RDF/XML
curl -H "Accept: application/rdf+xml" http://localhost:5000/products/42

# Standard JSON (unchanged)
curl -H "Accept: application/json" http://localhost:5000/products/42
```

## LLM Agent Workflow

An LLM coding assistant orchestrates the same CLI commands:

```
1. frank-cli extract --project MyApp.fsproj
2. frank-cli clarify --project MyApp.fsproj
   → Parse JSON output, resolve each decision point
3. frank-cli extract --project MyApp.fsproj  (with resolved clarifications)
4. frank-cli validate --project MyApp.fsproj
   → Check for remaining issues
5. frank-cli diff --project MyApp.fsproj
   → Review changes
6. frank-cli compile --project MyApp.fsproj
7. dotnet build
```

All commands output structured JSON by default. Add `--text` for human-readable output.
