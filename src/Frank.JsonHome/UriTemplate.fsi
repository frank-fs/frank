namespace Frank.JsonHome

/// Translation between ASP.NET routing templates and RFC 6570 URI Templates.
module UriTemplate =

    /// Rewrites an ASP.NET route template as an RFC 6570 URI Template, dropping
    /// inline constraints, optional markers, and default values, and mapping
    /// catch-all segments onto reserved expansion.
    val ofRouteTemplate: routeTemplate: string -> string

    /// The variable names appearing in a route template, in order.
    val variables: routeTemplate: string -> string list

    /// True when the template contains at least one variable.
    val isTemplated: routeTemplate: string -> bool
