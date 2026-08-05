namespace Frank.Validation

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    /// HttpContext.Items key a conforming request's parsed graph is stashed under, so the handler
    /// doesn't re-parse the body it already validated. Stays internal on purpose -- consumers read
    /// the graph through `Validation.tryGetValidatedGraph`, never by knowing this string.
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

/// The one piece of this package a downstream HANDLER (in a consuming application, a different
/// assembly) needs at request time. Deliberately NOT [<AutoOpen>]:
/// `Validation.tryGetValidatedGraph` reads better at the call site than a bare
/// `tryGetValidatedGraph`, and the module has exactly one member.
module Validation =
    /// The graph the interceptor already parsed and validated for this request, if there was one.
    ///
    /// `Some graph` for a POST/PUT/PATCH `application/ld+json` request to a
    /// `useValidation`-declared resource that CONFORMED -- so a handler never has to re-parse the
    /// body it was just handed. `None` for every other request: an unvalidated resource, a
    /// non-matching method or Content-Type, or a request that never reached the handler at all (a
    /// violating body 422s before this could be asked).
    val tryGetValidatedGraph: ctx: HttpContext -> VDS.RDF.IGraph option
