namespace Frank.LinkedData.Rdf

/// Shared helpers for extracting local names and namespace URIs from RDF URIs.
module RdfUriHelpers =

    /// Extract local name from URI (after last '/' or '#').
    let localName (uri: string) =
        let lastHash = uri.LastIndexOf('#')
        let lastSlash = uri.LastIndexOf('/')
        let idx = max lastHash lastSlash

        if idx >= 0 && idx < uri.Length - 1 then
            uri.Substring(idx + 1)
        else
            uri

    /// Extract namespace URI (up to and including last '/' or '#').
    let namespaceUri (uri: string) =
        let lastHash = uri.LastIndexOf('#')
        let lastSlash = uri.LastIndexOf('/')
        let idx = max lastHash lastSlash
        if idx >= 0 then uri.Substring(0, idx + 1) else uri
