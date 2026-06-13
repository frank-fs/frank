namespace Frank.Discovery

/// Local mirror of the AlpsDescriptor type emitted by GenerateDiscoveryTask.
/// Consumers inject values of this type rather than depending on the generated module.
type AlpsDescriptor =
    { Id: string
      Type: string
      Doc: string option
      Href: string option }

/// Configuration injected into Frank.Discovery middleware.
type DiscoveryConfig =
    {
        /// Base URI for ALPS profiles, e.g. "/alps".
        ProfileBaseUri: string
        /// Descriptor maps keyed by F# type full name.
        AlpsDescriptors: Map<string, AlpsDescriptor list>
        /// RFC 8288 Link header values keyed by F# type full name.
        DescribedByLinks: Map<string, string list>
        /// Route where JSON Home document is served (default "/").
        HomeRoute: string
    }

    static member Default =
        { ProfileBaseUri = "/alps"
          AlpsDescriptors = Map.empty
          DescribedByLinks = Map.empty
          HomeRoute = "/" }
