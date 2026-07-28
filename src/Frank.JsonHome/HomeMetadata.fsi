namespace Frank.JsonHome

/// Whether a resource is still current, per the JSON Home "status" hint.
[<RequireQualifiedAccess>]
type ResourceStatus =
    | Deprecated
    | Gone

/// The link relation type keying this resource in the home document.
type RelMetadata = { Rel: string }

/// The absolute URI identifying a template variable's semantics.
type HrefVarMetadata = { Name: string; Uri: string }

/// Documentation for this resource's link relation type.
type DocsMetadata = { Uri: string }

/// This resource's status hint.
type StatusMetadata = { Status: ResourceStatus }
