# Quickstart: Frank.Validation

**Feature**: 005-shacl-validation-from-fsharp-types
**Date**: 2026-03-07

## Adding Frank.Validation to a Project

Add a project reference to `Frank.Validation` (which transitively brings in Frank.LinkedData and Frank.Auth):

```xml
<ItemGroup>
  <ProjectReference Include="../Frank.Validation/Frank.Validation.fsproj" />
</ItemGroup>
```

Or, when published as a NuGet package:

```xml
<ItemGroup>
  <PackageReference Include="Frank.Validation" Version="7.2.0" />
</ItemGroup>
```

## Basic Usage with the `validate` CE Operation

Define an F# record type as your domain model, then use `validate` inside a `resource` CE to enable SHACL validation:

```fsharp
open Frank.Builder
open Frank.Validation

type CreateCustomer =
    { Name: string
      Email: string
      Age: int
      Notes: string option }

let customers =
    resource "/customers" {
        validate typeof<CreateCustomer>
        post (fun ctx -> task {
            // This handler only runs if the request body
            // passes SHACL validation against CreateCustomer's shape.
            // Invalid requests never reach this code.
            let! customer = ctx.ReadFromJsonAsync<CreateCustomer>()
            // ... handle valid customer
            ctx.Response.StatusCode <- 201
        })
    }
```

Register the validation middleware in the pipeline (after auth, before routing):

```fsharp
open Frank.Validation

let app =
    webHost [||] {
        useAuth        // Frank.Auth middleware (runs first)
        useValidation  // Frank.Validation middleware (runs second)
        resource "/customers" {
            validate typeof<CreateCustomer>
            post handleCreateCustomer
        }
    }
```

The derived SHACL shape for `CreateCustomer` will have:
- `Name`: `sh:datatype xsd:string`, `sh:minCount 1`
- `Email`: `sh:datatype xsd:string`, `sh:minCount 1`
- `Age`: `sh:datatype xsd:integer`, `sh:minCount 1`
- `Notes`: `sh:datatype xsd:string`, `sh:minCount 0` (option type)

## Validation Failure Response

When a client sends an invalid request, they receive a 422 response. The format depends on the `Accept` header.

### Standard JSON Client (`Accept: application/json`)

```http
POST /customers HTTP/1.1
Content-Type: application/json
Accept: application/json

{"Email": "alice@example.com", "Age": "not-a-number"}
```

Response (RFC 9457 Problem Details):

```http
HTTP/1.1 422 Unprocessable Content
Content-Type: application/problem+json

{
  "type": "urn:frank:validation:shacl-violation",
  "title": "Validation Failed",
  "status": 422,
  "detail": "Request body violates 2 SHACL constraints",
  "errors": [
    {
      "path": "$.Name",
      "constraint": "sh:minCount",
      "message": "Field 'Name' is required (sh:minCount 1)",
      "value": null
    },
    {
      "path": "$.Age",
      "constraint": "sh:datatype",
      "message": "Field 'Age' must be xsd:integer, got string",
      "value": "not-a-number"
    }
  ]
}
```

### Semantic Client (`Accept: application/ld+json`)

The same invalid request with a semantic `Accept` header returns a SHACL ValidationReport serialized as JSON-LD:

```http
HTTP/1.1 422 Unprocessable Content
Content-Type: application/ld+json

{
  "@context": {
    "sh": "http://www.w3.org/ns/shacl#",
    "xsd": "http://www.w3.org/2001/XMLSchema#"
  },
  "@type": "sh:ValidationReport",
  "sh:conforms": false,
  "sh:result": [
    {
      "@type": "sh:ValidationResult",
      "sh:focusNode": { "@id": "_:request" },
      "sh:resultPath": { "@id": "urn:frank:property:Name" },
      "sh:sourceConstraintComponent": { "@id": "sh:MinCountConstraintComponent" },
      "sh:resultMessage": "Field 'Name' is required (sh:minCount 1)",
      "sh:resultSeverity": { "@id": "sh:Violation" }
    },
    {
      "@type": "sh:ValidationResult",
      "sh:focusNode": { "@id": "_:request" },
      "sh:resultPath": { "@id": "urn:frank:property:Age" },
      "sh:value": "not-a-number",
      "sh:sourceConstraintComponent": { "@id": "sh:DatatypeConstraintComponent" },
      "sh:resultMessage": "Field 'Age' must be xsd:integer, got string",
      "sh:resultSeverity": { "@id": "sh:Violation" }
    }
  ]
}
```

## Adding Custom Constraints

Extend auto-derived shapes with constraints the type system cannot express:

```fsharp
open Frank.Validation

let customers =
    resource "/customers" {
        validate typeof<CreateCustomer>
        customConstraint "Email" (PatternConstraint @"^[^@]+@[^@]+\.[^@]+$")
        customConstraint "Age" (MinInclusiveConstraint 0)
        customConstraint "Age" (MaxInclusiveConstraint 150)
        post handleCreateCustomer
    }
```

Custom constraints are additive -- they tighten the shape but cannot weaken it. Attempting to make a required field optional at startup raises an `InvalidOperationException`.

## Capability-Dependent Validation

Use `validateWithCapabilities` to vary shapes based on the authenticated principal:

```fsharp
type UpdateOrder =
    { Status: string
      Notes: string option }

let orders =
    resource "/orders/{orderId}" {
        validateWithCapabilities typeof<UpdateOrder> [
            forClaim "role" ["admin"] (fun shape ->
                shape // admins: no extra restrictions on Status
            )
            forClaim "role" ["user"] (fun shape ->
                { shape with
                    Properties =
                        shape.Properties
                        |> List.map (fun p ->
                            if p.Path = "Status"
                            then { p with InValues = Some ["Submitted"; "Cancelled"] }
                            else p) }
            )
        ]
        put handleUpdateOrder
    }
```

An admin POSTing `Status: "Refunded"` passes validation. A regular user POSTing the same value gets a 422 with an `sh:in` constraint violation.
