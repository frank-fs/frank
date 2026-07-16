/// JSON Home (draft-nottingham-json-home) serializer. Codegen/interop plumbing —
/// not part of Frank.Discovery's public API surface; visible only to
/// Frank.Discovery.Tests via InternalsVisibleTo (#392).
module internal Frank.Discovery.JsonHomeSerializer

/// Extract bare variable names from a URI Template per RFC 6570.
/// `{id}` → ["id"]; `{+base}` → ["base"]; `{x,y}` → ["x"; "y"]; `{x:3}` → ["x"]; `{list*}` → ["list"].
val extractTemplateVars: template: string -> string list

/// Serialize resource entries to a JSON Home document. A URI Template (contains
/// '{') is written as `href-template` with a companion `href-vars` object (JSON
/// Home draft §4.2). A fixed URI is written as `href` (RFC draft-nottingham
/// -json-home-06).
val serialize: resources: JsonHomeResource list -> string
