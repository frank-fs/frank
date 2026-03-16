# Quickstart: OPTIONS and Link Header Discovery

**Date**: 2026-03-16
**Feature**: 019-options-link-discovery

## Overview

Frank.Discovery adds HTTP-native discovery to Frank applications. Agents can learn available media types and methods via OPTIONS responses and Link headers (RFC 8288). Each extension package (Frank.LinkedData, Frank.Statecharts) contributes its media types automatically.

## Prerequisites

- Frank 7.3.0+ (includes `DiscoveryMediaType` in core)
- Frank.Discovery NuGet package
- At least one extension that contributes media types (e.g., Frank.LinkedData)

## Basic Usage

### Enable OPTIONS Discovery

```fsharp
open Frank.Builder
open Frank.Discovery
open Frank.LinkedData

webHost [||] {
    useDefaults
    useLinkedData
    useOptionsDiscovery  // Enable implicit OPTIONS responses

    resource "/items" {
        name "Items"
        linkedData           // Marks resource for LinkedData content negotiation
        get (fun ctx -> ctx.Response.WriteAsync("items"))
        post (fun ctx -> ctx.Response.WriteAsync("created"))
    }
}
```

Sending `OPTIONS /items` returns:
```
HTTP/1.1 200 OK
Allow: GET, POST, OPTIONS
```

The response also includes media type information from the LinkedData extension.

### Enable Link Headers

```fsharp
webHost [||] {
    useDefaults
    useLinkedData
    useLinkHeaders  // Enable RFC 8288 Link header injection

    resource "/items" {
        name "Items"
        linkedData
        get (fun ctx -> ctx.Response.WriteAsync("items"))
    }
}
```

Sending `GET /items` returns:
```
HTTP/1.1 200 OK
Link: </items>; rel="describedby"; type="application/ld+json"
Link: </items>; rel="describedby"; type="text/turtle"
Link: </items>; rel="describedby"; type="application/rdf+xml"
Content-Type: text/plain

items
```

### Enable Both (Convenience)

```fsharp
webHost [||] {
    useDefaults
    useLinkedData
    useDiscovery  // Enables both OPTIONS discovery and Link headers

    resource "/items" {
        name "Items"
        linkedData
        get (fun ctx -> ctx.Response.WriteAsync("items"))
    }
}
```

## Key Behaviors

### Resources Without Semantic Markers

Resources without `linkedData` or other semantic markers are unaffected:

```fsharp
resource "/health" {
    get (fun ctx -> ctx.Response.WriteAsync("ok"))
}
```

- OPTIONS returns only `Allow: GET, OPTIONS` (no media types)
- GET returns no Link headers

### Explicit OPTIONS Handlers Take Precedence

```fsharp
resource "/custom" {
    get (fun ctx -> ctx.Response.WriteAsync("data"))
    options (fun ctx -> ctx.Response.WriteAsync("my custom OPTIONS response"))
}
```

The explicit `options` handler runs instead of the implicit discovery response.

### CORS Coexistence

CORS preflight requests (OPTIONS with `Access-Control-Request-Method` header) are passed through to the CORS middleware. Discovery only handles non-CORS OPTIONS requests.

### Link Headers on 2xx Only

Link headers are only added to successful responses (2xx status codes) for GET and HEAD requests. Error responses do not include discovery Link headers.

## Multiple Extensions

When multiple extensions contribute media types, they are all included:

```fsharp
resource "/workflow" {
    name "Workflow"
    linkedData
    stateMachine myMachine
    get (fun ctx -> ctx.Response.WriteAsync("workflow state"))
}
```

OPTIONS returns methods and media types from both LinkedData and Statecharts. Link headers include profiles from both extensions. Duplicate media types are deduplicated.
