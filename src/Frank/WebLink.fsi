namespace Frank.Builder

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

/// One RFC 8288 Link header entry.
type WebLink =
    { /// URI-Reference the link points at.
      Target: string
      /// The link relation type, e.g. "home", "service-desc".
      Rel: string
      /// Additional target attributes, e.g. "type", "title", "hreflang", in declaration order.
      Params: (string * string) list }

/// Marks an endpoint-metadata entry as a resource-scoped Link contribution.
/// Internal: ResourceBuilder.fs attaches these to EndpointBuilder.Metadata;
/// WebLink.useResourceScopedLinks reads them back at request time. Not part
/// of the public authoring surface -- callers only see ResourceBuilder's
/// `link` operation.
type internal ResourceLinkProvider = ResourceLinkProvider of (HttpContext -> WebLink seq)

module WebLink =

    /// Formats one WebLink as an RFC 8288 field value, escaping backslashes
    /// and double quotes in quoted parameter values (rel and every param value).
    val format: link: WebLink -> string

    /// Installs app-wide Link contributions. On each request, calls every
    /// provider and, if any returned at least one WebLink, appends them all
    /// as a single Link header via Response.OnStarting -- surviving
    /// exception-handling middleware regenerating the response, and still
    /// applying to responses for unmatched routes. Splice this in before
    /// BeforeRoutingMiddleware runs.
    val useAppWideLinks: providers: (HttpContext -> WebLink seq) list -> app: IApplicationBuilder -> IApplicationBuilder

    /// Installs resource-scoped Link contributions. Reads the matched
    /// endpoint's metadata (populated by ResourceBuilder's `link` operation)
    /// and, if any resource-scoped providers are present, appends their
    /// links the same way useAppWideLinks does. A request matching no
    /// endpoint contributes nothing. Splice this in after UseRouting runs
    /// and before Middleware runs, since it needs the matched endpoint.
    val useResourceScopedLinks: app: IApplicationBuilder -> IApplicationBuilder
