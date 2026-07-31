namespace Frank.Builder

/// One RFC 8288 Link header entry.
type WebLink =
    { /// URI-Reference the link points at.
      Target: string
      /// The link relation type, e.g. "home", "service-desc".
      Rel: string
      /// Additional target attributes, e.g. "type", "title", "hreflang", in declaration order.
      Params: (string * string) list }

module WebLink =

    /// Formats one WebLink as an RFC 8288 field value, escaping backslashes
    /// and double quotes in quoted parameter values (rel and every param value).
    val format: link: WebLink -> string
