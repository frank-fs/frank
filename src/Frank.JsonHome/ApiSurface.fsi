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
      Docs: string option
      Status: ResourceStatus option
      /// Endpoint metadata, retained for authorization filtering.
      Metadata: obj list }

module ApiSurface =

    /// Projects ApiExplorer descriptions into entry-point resources, grouping by
    /// route template. Descriptions without a RelMetadata are excluded.
    val ofApiDescriptions: descriptions: ApiDescription seq -> ResourceDescription list
