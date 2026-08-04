namespace Frank.Validation

open Microsoft.AspNetCore.Builder
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    /// HttpContext.Items key a conforming request's parsed graph is stashed under, so the handler
    /// doesn't re-parse the body it already validated.
    val internal ValidatedGraphKey: string

    /// The one app-wide interceptor: reads ValidationMetadata off the matched endpoint (set by
    /// `useValidation` on resource{ }), and for POST/PUT/PATCH application/ld+json requests to a
    /// validated resource, buffers/parses/validates the body before the handler runs. A no-op
    /// pass-through otherwise. Exposed (not private) so tests can wire it directly via TestServer,
    /// the same way test/Frank.Tests/ResponseLinkTests.fs tests WebLink.useResourceScopedLinks.
    val internal useValidationMiddleware: app: IApplicationBuilder -> IApplicationBuilder

    type WebHostBuilder with
        /// Registers the interceptor into the pipeline, once, app-wide. Composes into the same
        /// Middleware field useOpenApi does -- runs after UseRouting(), so ctx.GetEndpoint() is
        /// already populated (verified against src/Frank/WebHostBuilder.fs's Run).
        [<CustomOperation("useValidation")>]
        member UseValidation: spec: WebHostSpec -> WebHostSpec
