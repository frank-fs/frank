# API Contracts

This feature does not introduce new API endpoints. The existing endpoints remain unchanged:

## Existing Endpoints (No Changes)

### Contact Resource
- `GET /contacts/{id}` - Load contact, establish SSE
- `GET /contacts/{id}/edit` - Switch to edit mode
- `PUT /contacts/{id}` - Save contact changes

### Fruits Resource
- `GET /fruits` - Load all fruits, establish SSE
- `GET /fruits?q={query}` - Filter fruits by search term

### Items Resource
- `GET /items` - Load items table, establish SSE
- `DELETE /items/{id}` - Remove an item

### Users Resource
- `GET /users` - Load users table, establish SSE
- `PUT /users/bulk?status={active|inactive}` - Bulk status update

### Registration Resource
- `GET /registrations/form` - Load registration form, establish SSE
- `POST /registrations/validate` - Validate form fields
- `POST /registrations` - Submit registration

## Change Summary

**What changes**: Internal SSE channel routing (5 channels → 1 channel)

**What stays the same**: All HTTP endpoints, request/response formats, HTML output
