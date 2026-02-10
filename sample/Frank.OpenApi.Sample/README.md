# Frank.OpenApi Sample - Product Catalog API

This sample demonstrates Frank.OpenApi features with a complete product catalog REST API.

## Features Demonstrated

### HandlerBuilder Computation Expression

All API endpoints use the `handler` computation expression to define OpenAPI metadata:

- **name** - Sets the OpenAPI operationId
- **summary** - Brief operation description
- **description** - Detailed operation documentation
- **tags** - Categorize endpoints (Products, Admin, Search, etc.)
- **produces** - Define response types with status codes
- **producesEmpty** - Define empty responses (204, etc.)
- **accepts** - Define request body types
- **handle** - Handler implementation (supports Task, Task<'a>, Async<unit>, Async<'a>)

### F# Type Schema Generation

The API uses F# types that are automatically converted to JSON schemas:

- **Records** - `Product`, `CreateProductRequest`, `UpdateProductRequest`
- **Optional fields** - `Description: string option`
- **Discriminated unions** - `Category`, `ProductResponse`
- **Collections** - `Product list`, `Set<string>`, `string list`
- **Primitive types** - `Guid`, `decimal`, `bool`

### Content Negotiation

The `/api/products/{id}/negotiate` endpoint demonstrates content negotiation:

```fsharp
produces typeof<Product> 200 [ "application/json"; "application/xml" ]
```

### Mixed Handlers

The `/health` endpoint uses a plain `RequestDelegate`, demonstrating backward compatibility with non-OpenAPI handlers.

## Running the Sample

```bash
cd sample/Frank.OpenApi
dotnet run
```

The application will start on `http://localhost:5000` (HTTP) and `https://localhost:5001` (HTTPS).

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | API information |
| GET | `/health` | Health check (plain handler) |
| GET | `/api/products` | List all products |
| POST | `/api/products` | Create a new product |
| GET | `/api/products/{id}` | Get product by ID |
| PUT | `/api/products/{id}` | Update a product |
| DELETE | `/api/products/{id}` | Delete a product |
| POST | `/api/products/search` | Search products with filters |
| GET | `/api/products/{id}/negotiate` | Content negotiation example |
| GET | `/openapi/v1.json` | OpenAPI document |

## OpenAPI Document

View the generated OpenAPI document:

```bash
curl http://localhost:5000/openapi/v1.json | jq
```

Or visit `http://localhost:5000/openapi/v1.json` in your browser.

## Example Requests

### Get API Information

```bash
curl http://localhost:5000/
```

### List Products

```bash
curl http://localhost:5000/api/products
```

### Get Product by ID

```bash
curl http://localhost:5000/api/products/11111111-1111-1111-1111-111111111111
```

### Create Product

```bash
curl -X POST http://localhost:5000/api/products \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Wireless Mouse",
    "description": "Ergonomic wireless mouse",
    "price": 29.99,
    "category": "Electronics",
    "tags": ["tech", "accessories"]
  }'
```

### Update Product

```bash
curl -X PUT http://localhost:5000/api/products/11111111-1111-1111-1111-111111111111 \
  -H "Content-Type: application/json" \
  -d '{
    "price": 999.99,
    "inStock": false
  }'
```

### Delete Product

```bash
curl -X DELETE http://localhost:5000/api/products/11111111-1111-1111-1111-111111111111
```

### Search Products

```bash
curl -X POST http://localhost:5000/api/products/search \
  -H "Content-Type: application/json" \
  -d '{
    "category": "Electronics",
    "minPrice": 0,
    "maxPrice": 100,
    "inStockOnly": true
  }'
```

## OpenAPI Schema Examples

The generated OpenAPI document includes schemas for all F# types:

### Product Schema

```json
{
  "type": "object",
  "properties": {
    "id": { "type": "string", "format": "uuid" },
    "name": { "type": "string" },
    "description": { "type": "string", "nullable": true },
    "price": { "type": "number", "format": "decimal" },
    "category": { "type": "string", "enum": ["Electronics", "Books", "Clothing", "Home"] },
    "tags": { "type": "array", "items": { "type": "string" } },
    "inStock": { "type": "boolean" }
  },
  "required": ["id", "name", "price", "category", "tags", "inStock"]
}
```

### Category Schema (Discriminated Union)

```json
{
  "type": "string",
  "enum": ["Electronics", "Books", "Clothing", "Home"]
}
```

## Code Organization

- **Domain.fs** - F# domain types and in-memory store
- **Handlers.fs** - Handler definitions using HandlerBuilder CE
- **Program.fs** - Resource definitions and web host configuration

## Key Takeaways

1. **Declarative API Design** - OpenAPI metadata is defined alongside handler logic
2. **Type Safety** - F# types are automatically converted to JSON schemas
3. **Zero Boilerplate** - No separate OpenAPI spec file to maintain
4. **Backward Compatible** - Mix OpenAPI and non-OpenAPI handlers in the same application
5. **Content Negotiation** - First-class support for multiple content types
