# Frank.JsonHome Sample

Demonstrates [`Frank.JsonHome`](../../src/Frank.JsonHome) serving a [JSON Home](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06) document, and — the most novel part — filtering it by the authenticated principal via [`Frank.Auth`](../../src/Frank.Auth).

## Run it

```bash
dotnet run --project sample/Frank.JsonHome.Sample/
```

The app listens on the URL Kestrel prints on startup (`http://localhost:5000` by default). Substitute that below.

## What's here

| Resource | Discovery metadata | Notes |
|---|---|---|
| `GET /` | none | Plain landing page, not part of the document |
| `GET,POST /products` | `rel`, `docs` | Public |
| `GET,PUT,DELETE /products/{id}` | `rel`, `hrefVar` | Public, templated |
| `GET /admin/reports` | `rel`, `requireRole "admin"` | **Guarded** — see below |
| `GET /legacy/inventory` | `rel`, `deprecated`, `docs` | `status: "deprecated"` in the document |
| `GET /discontinued/catalog` | `rel`, `gone` | `status: "gone"`; the resource itself also answers 410 |

Authentication is a deliberately minimal `X-Api-Key` header scheme (`ApiKeyAuth.fs`) — enough to make the authorization filtering curl-able without standing up a real identity provider. A real app would use JWT Bearer, cookies, or OAuth via `Frank.Auth`'s `useAuthentication`; nothing about `Frank.JsonHome`'s filtering depends on which scheme is in use, since it reads `IAuthorizeData`/`AuthorizationPolicy` metadata, not the scheme itself.

Two keys are recognized: `admin-key` (role `admin`) and `user-key` (no roles).

## The interesting part: the document itself differs by principal

```bash
# Anonymous: admin-reports is absent
curl -s http://localhost:5000/.well-known/home.json | jq '.resources | keys'
# [
#   "tag:frank-fs.github.io,2026:discontinued-catalog",
#   "tag:frank-fs.github.io,2026:legacy-inventory",
#   "tag:frank-fs.github.io,2026:product",
#   "tag:frank-fs.github.io,2026:products"
# ]

# As admin: admin-reports appears
curl -s -H "X-Api-Key: admin-key" http://localhost:5000/.well-known/home.json | jq '.resources | keys'
# [
#   "tag:frank-fs.github.io,2026:admin-reports",
#   "tag:frank-fs.github.io,2026:discontinued-catalog",
#   "tag:frank-fs.github.io,2026:legacy-inventory",
#   "tag:frank-fs.github.io,2026:product",
#   "tag:frank-fs.github.io,2026:products"
# ]

# As a non-admin user: still absent -- it's role-based, not just "any authenticated user"
curl -s -H "X-Api-Key: user-key" http://localhost:5000/.well-known/home.json | jq '.resources | keys'
```

Because the document has at least one guarded resource, every response — anonymous or not — carries:

```
Cache-Control: private, no-cache
Vary: Authorization
```

so a shared cache can never serve one principal's document to another.

## Discovery filtering vs. actual access control

`Frank.JsonHome` only decides what's *listed*. `Frank.Auth`'s `requireRole "admin"` on the same resource is what actually protects it:

```bash
curl -i http://localhost:5000/admin/reports
# HTTP/1.1 401 Unauthorized

curl -i -H "X-Api-Key: admin-key" http://localhost:5000/admin/reports
# HTTP/1.1 200 OK
```

The document not listing a resource for you and the resource rejecting your request are two independent, consistent effects of the same `requireRole` declaration — not two features you have to wire up separately.

## Every response advertises the document

```bash
curl -sI http://localhost:5000/products | grep -i '^Link:'
# Link: </.well-known/home.json>; rel="home"

curl -sI http://localhost:5000/nope | grep -i '^Link:'
# Link: </.well-known/home.json>; rel="home"
```

Even a 404 carries it — that's where a lost client most needs the link.
