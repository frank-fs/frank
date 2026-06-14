namespace Frank.Discovery

/// One ALPS descriptor entry. `Type` is "semantic" for resources/fields, or
/// "safe"/"unsafe"/"idempotent" for HTTP-method action descriptors. No
/// state/transition nesting — per-role projection is Track A (v7.4.0).
type AlpsDescriptor =
    { Id: string
      Type: string
      Doc: string option
      Href: string option }

/// One JSON Home resource directory entry. Relation is a vocabulary IRI.
type JsonHomeResource =
    { Relation: string
      Href: string
      Allow: string list }

/// Endpoint metadata stamped by the `relation` CE operation. Carries the
/// vocabulary IRI for this resource so the middleware can build the JSON Home
/// directory at runtime. Must be a reference type (record satisfies this) because
/// EndpointMetadataCollection.GetMetadata<T> has a `class` constraint.
type ResourceRelationMetadata = { Relation: string }

/// Discovery configuration the middleware consumes. In the finished system this
/// is derived from the generated `GeneratedDiscovery` module (issue #326); until
/// then it is hand-authored in the application.
type DiscoveryConfig =
    {
        /// Route serving the ALPS profile, e.g. "/alps/tictactoe".
        ProfileUri: string
        /// Route serving the JSON Home document, e.g. "/".
        HomeRoute: string
        /// Flat ALPS descriptors (resource + field + action), vocabulary IRIs.
        AlpsDescriptors: AlpsDescriptor list
        /// External vocabulary Link header values, e.g.
        /// `<https://schema.org/Game>; rel="describedby"`.
        DescribedByLinks: string list
    }

    static member Empty =
        { ProfileUri = "/alps"
          HomeRoute = "/"
          AlpsDescriptors = []
          DescribedByLinks = [] }
