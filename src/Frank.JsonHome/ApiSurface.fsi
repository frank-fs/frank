namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer

/// One entry-point resource, described independently of any output format.
type ResourceDescription =
    { Rel: string
      Href: string
      IsTemplated: bool
      HrefVars: (string * string) list
      Methods: string list
      Formats: string list
      /// Request content types, keyed by HTTP method.
      Accepts: (string * string list) list
      AcceptRanges: string list
      AcceptPrefer: string list
      PreconditionRequired: Precondition list
      /// Authentication schemes, each with the protection spaces it covers.
      AuthSchemes: (string * string list) list
      Docs: string option
      Status: ResourceStatus option
      /// Endpoint metadata, retained for authorization filtering.
      Metadata: obj list
      /// Endpoint metadata for each HTTP method registered on this resource,
      /// retained separately from Metadata so authorization can be evaluated
      /// (and Methods/Accepts/Formats filtered) per method rather than merged
      /// across the whole resource.
      MethodMetadata: (string * obj list) list }

module ApiSurface =

    /// Projects ApiExplorer descriptions into entry-point resources, grouping by
    /// route template. Descriptions without a RelMetadata are excluded.
    val ofApiDescriptions: descriptions: ApiDescription seq -> ResourceDescription list
