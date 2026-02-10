# Data Model: OpenAPI Document Generation Support

**Feature Branch**: `016-openapi`
**Date**: 2026-02-09

## Entities

### HandlerDefinition

The core data type produced by the `HandlerBuilder` CE. Combines a request handler with OpenAPI metadata.

**Fields**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Handler | `RequestDelegate` | Yes | The HTTP request handler function |
| Name | `string option` | No | Operation name (maps to `operationId` in OpenAPI) |
| Summary | `string option` | No | Short summary (maps to `summary` in OpenAPI) |
| Description | `string option` | No | Detailed description (maps to `description` in OpenAPI) |
| Tags | `string list` | No | Grouping tags (maps to `tags` in OpenAPI) |
| Produces | `ProducesInfo list` | No | Response type/status code annotations |
| Accepts | `AcceptsInfo list` | No | Request body type annotations |

**Validation rules**:
- `Handler` must be set (builder should error at compile time or runtime if missing)
- `Name` should be unique per resource (ASP.NET Core uses it as `operationId`)
- `Produces` entries should have unique status codes per content type

### ProducesInfo

Describes a possible response from an endpoint.

**Fields**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| StatusCode | `int` | Yes | HTTP status code (e.g., 200, 404) |
| ResponseType | `Type option` | No | CLR type of the response body (None for empty responses) |
| ContentTypes | `string list` | No | Content types (defaults to `["application/json"]`) |
| Description | `string option` | No | Description of this response |

### AcceptsInfo

Describes an accepted request body type for an endpoint.

**Fields**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| RequestType | `Type` | Yes | CLR type of the request body |
| ContentTypes | `string list` | No | Accepted content types (defaults to `["application/json"]`) |
| IsOptional | `bool` | No | Whether the request body is optional (defaults to `false`) |

## Relationships

```text
HandlerBuilder CE
    └── produces → HandlerDefinition
                        ├── has many → ProducesInfo
                        └── has many → AcceptsInfo

HandlerDefinition
    └── consumed by → ResourceBuilder type extensions
                            └── converts to → (string * RequestDelegate) handler entry
                                            + (EndpointBuilder -> unit) metadata conventions
```

## Metadata Conversion

At registration time (when `ResourceBuilder` processes a `HandlerDefinition`), the definition is split into:

1. **Handler**: `(httpMethod, definition.Handler)` added to `ResourceSpec.Handlers`
2. **Metadata conventions**: A list of `EndpointBuilder -> unit` functions added to `ResourceSpec.Metadata`:
   - `Name` → adds `EndpointNameMetadata`
   - `Summary` → adds `EndpointSummaryMetadata`
   - `Description` → adds `EndpointDescriptionMetadata`
   - `Tags` → adds `TagsMetadata`
   - Each `ProducesInfo` → adds `ProducesResponseTypeMetadata`
   - Each `AcceptsInfo` → adds `AcceptsMetadata`

## State Transitions

N/A — All data is immutable and constructed at startup time. No runtime state transitions.
