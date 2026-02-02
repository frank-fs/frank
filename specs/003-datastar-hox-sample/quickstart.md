# Quickstart: Frank.Datastar.Hox Sample

**Feature**: 003-datastar-hox-sample
**Date**: 2026-01-27

## Prerequisites

- .NET 10.0 SDK installed
- curl (for testing)

## Build and Run

```bash
# From repository root
cd /Users/ryanr/Code/frank

# Build the sample
dotnet build sample/Frank.Datastar.Hox

# Run the sample
dotnet run --project sample/Frank.Datastar.Hox
```

The server starts on http://localhost:5000

## Quick Verification

Open http://localhost:5000 in a browser to see the interactive demo.

## Run Automated Tests

```bash
# From the sample directory (or copy test.sh there first)
cd sample/Frank.Datastar.Hox
./test.sh 5000
```

Expected output: All tests pass (tests 11-28)

## Manual Testing Examples

### Contact Click-to-Edit

```bash
# Load contact (establishes SSE)
curl -m 2 http://localhost:5000/contacts/1

# Switch to edit mode (fire-and-forget)
curl http://localhost:5000/contacts/1/edit

# Save changes (fire-and-forget)
curl -X PUT http://localhost:5000/contacts/1 \
  -H "Content-Type: application/json" \
  -d '{"firstName":"John","lastName":"Doe","email":"john@example.com"}'
```

### Fruits Search

```bash
# Load full list (establishes SSE)
curl -m 2 http://localhost:5000/fruits

# Search (fire-and-forget)
curl http://localhost:5000/fruits?q=ap
```

### Items Delete

```bash
# Load items (establishes SSE)
curl -m 2 http://localhost:5000/items

# Delete item (fire-and-forget)
curl -X DELETE http://localhost:5000/items/1
```

### Users Bulk Update

```bash
# Load users (establishes SSE)
curl -m 2 http://localhost:5000/users

# Bulk activate (fire-and-forget)
curl -X PUT "http://localhost:5000/users/bulk?status=active" \
  -H "Content-Type: application/json" \
  -d '{"selections":[false,true,false,true]}'
```

### Registration Validation

```bash
# Load form (establishes SSE)
curl -m 2 http://localhost:5000/registrations/form

# Validate (fire-and-forget)
curl -X POST http://localhost:5000/registrations/validate \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","firstName":"John","lastName":"Doe"}'

# Submit (fire-and-forget)
curl -X POST http://localhost:5000/registrations \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","firstName":"John","lastName":"Doe"}'
```

## Key Differences from Basic Sample

| Aspect | Basic Sample | Hox Sample |
|--------|--------------|------------|
| HTML Generation | F# string templates (`$"""..."""`) | Hox DSL (`h("selector", children)`) |
| Imports | None for HTML | `open Hox`, `open Hox.Core`, `open Hox.Rendering` |
| Render Pattern | Returns string directly | Builds Node, then `Render.asString` |
| Attributes | String interpolation | CSS selector syntax `[attr=value]` |

## Troubleshooting

### Server won't start
- Check .NET 10.0 SDK is installed: `dotnet --version`
- Check port 5000 is not in use

### Tests fail with connection refused
- Ensure server is running: `dotnet run --project sample/Frank.Datastar.Hox`
- Check correct port (default 5000)

### SSE tests timeout
- This is expected behavior - SSE connections stay open
- curl with `-m 2` timeout is used for SSE endpoints
