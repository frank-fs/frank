namespace Frank.Validation

open System
open VDS.RDF.JsonLd

/// Document loader delegate: given a URI and loader options, returns a RemoteDocument.
type JsonLdDocumentLoader = Func<Uri, JsonLdLoaderOptions, RemoteDocument>

type ValidationConfig =
    { Shapes: VDS.RDF.Shacl.ShapesGraph
      ContextLoader: JsonLdDocumentLoader }
