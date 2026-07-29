module Sample.JsonHome.Program

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Frank.Builder
open Frank.Auth
open Frank.JsonHome
open Sample.JsonHome.ApiKeyAuth

// Handlers -- plain JSON responses, since the point of this sample is the
// discovery document and its authorization filtering, not the payloads.

let private json (value: obj) : RequestDelegate =
    RequestDelegate(fun ctx -> ctx.Response.WriteAsJsonAsync value)

let private apiInfo =
    json
        {| name = "Frank.JsonHome Sample API"
           home = "/.well-known/home.json"
           note = "GET /.well-known/home.json anonymously, then again with 'X-Api-Key: admin-key' -- the resources listed differ." |}

let private listProducts =
    json [ {| id = 1; name = "Widget" |}; {| id = 2; name = "Gadget" |} ]

let private getProduct = json {| id = 1; name = "Widget" |}
let private createProduct = json {| id = 3; name = "New Product" |}
let private updateProduct = json {| id = 1; name = "Updated Widget" |}
let private deleteProduct: RequestDelegate = RequestDelegate(fun ctx -> ctx.Response.WriteAsync "")

let private adminReports =
    json {| revenue = 42000; note = "Only visible in the document to an authenticated admin" |}

let private legacyInventory = json {| note = "Deprecated -- still works, but the document says so" |}

// Resources

let private rootResource = resource "/" { get apiInfo }

let private productsResource =
    resource "/products" {
        rel "tag:frank-fs.github.io,2026:products"
        docs "https://github.com/frank-fs/frank/blob/master/sample/Frank.JsonHome.Sample/README.md"
        get listProducts
        post createProduct
    }

let private productByIdResource =
    resource "/products/{id}" {
        rel "tag:frank-fs.github.io,2026:product"
        hrefVar "id" "https://frank-fs.github.io/param/product-id"
        get getProduct
        put updateProduct
        delete deleteProduct
    }

let private adminReportsResource =
    resource "/admin/reports" {
        rel "tag:frank-fs.github.io,2026:admin-reports"
        requireRole "admin"
        get adminReports
    }

let private legacyResource =
    resource "/legacy/inventory" {
        rel "tag:frank-fs.github.io,2026:legacy-inventory"
        docs "https://github.com/frank-fs/frank/blob/master/sample/Frank.JsonHome.Sample/README.md"
        deprecated
        get legacyInventory
    }

let private discontinuedResource =
    resource "/discontinued/catalog" {
        rel "tag:frank-fs.github.io,2026:discontinued-catalog"
        gone
        get(RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 410
            ctx.Response.WriteAsync "Gone"))
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        useAuthentication (fun auth ->
            // DefaultScheme is what lets UseAuthentication populate ctx.User
            // without every [<Authorize>]-guarded resource having to name a
            // scheme explicitly. Frank.Auth's useAuthentication only calls
            // AddAuthentication() with no scheme, so this sample sets it via
            // the builder's own Services rather than through Frank.Auth.
            auth.Services.Configure<AuthenticationOptions>(fun (o: AuthenticationOptions) ->
                o.DefaultScheme <- SchemeName
                o.DefaultAuthenticateScheme <- SchemeName)
            |> ignore

            auth.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(SchemeName, fun _ -> ()))

        useAuthorization

        useJsonHome (fun opts ->
            { opts with
                Title = Some "Frank.JsonHome Sample API"
                Links = [ "author", "mailto:sample@example.com" ] })

        resource rootResource
        resource productsResource
        resource productByIdResource
        resource adminReportsResource
        resource legacyResource
        resource discontinuedResource
    }

    0
