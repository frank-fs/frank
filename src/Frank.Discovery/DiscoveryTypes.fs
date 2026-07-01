namespace Frank.Discovery

/// One ALPS descriptor entry. `Type` is "semantic" for data descriptors or
/// "unsafe" for action descriptors (e.g. schema:MoveAction). `Descriptors` holds
/// nested field/input descriptors (AC1: field-shape nesting). `Rt` is the return
/// type IRI for action descriptors. Per-role state/transition nesting is Track A.
type AlpsDescriptor =
    { Id: string
      Type: string
      Doc: string option
      Href: string option
      Descriptors: AlpsDescriptor list
      Rt: string option }

/// One JSON Home resource directory entry. Relation is a vocabulary IRI.
/// HrefVars maps each URI-template variable name to its absolute meaning IRI
/// (json-home draft §4.2). An empty-string value means the variable's meaning
/// could not be derived from the semantic model; this should not occur for
/// template variables whose names correspond to any confirmed resource field.
type JsonHomeResource =
    { Relation: string
      Href: string
      Allow: string list
      HrefVars: Map<string, string> }

/// Endpoint metadata stamped by the `relation` CE operation. Carries the
/// vocabulary IRI for this resource so the middleware can build the JSON Home
/// directory at runtime. Must be a reference type (record satisfies this) because
/// EndpointMetadataCollection.GetMetadata<T> has a `class` constraint.
type ResourceRelationMetadata = { Relation: string }

/// Discovery configuration the middleware consumes. Derived from the generated
/// `GeneratedDiscovery` module (MSBuild codegen, issue #326) in the application.
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
        /// Per-resource mapping: relation IRI → (template variable name → absolute meaning IRI).
        /// Built at codegen time from the resolved model's field IRIs. Used by the middleware
        /// to populate JsonHomeResource.HrefVars for json-home §4.2 href-vars emission.
        ResourceHrefVars: Map<string, Map<string, string>>
    }

    static member Empty =
        { ProfileUri = "/alps"
          HomeRoute = "/"
          AlpsDescriptors = []
          DescribedByLinks = []
          ResourceHrefVars = Map.empty }
