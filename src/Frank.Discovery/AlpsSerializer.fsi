/// ALPS+JSON serializer. Codegen/interop plumbing — not part of Frank.Discovery's
/// public API surface; visible only to Frank.Discovery.Tests and
/// Frank.Cli.Core.Tests (which exercises it directly against emitter output)
/// via InternalsVisibleTo (#392).
module internal Frank.Discovery.AlpsSerializer

/// Serialize a descriptor list to an ALPS+JSON document. Field descriptors
/// are nested inside their class descriptor (AC1). Action descriptors carry
/// `rt` for the return type. Leaf descriptors emit no `descriptor` array.
val serialize: descriptors: AlpsDescriptor list -> string
