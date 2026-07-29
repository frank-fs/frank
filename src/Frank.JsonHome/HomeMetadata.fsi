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

/// A precondition a resource requires on state-changing requests, per the
/// JSON Home "preconditionRequired" hint.
[<RequireQualifiedAccess>]
type Precondition =
    | ETag
    | LastModified

/// HTTP range-specifiers this resource accepts.
type AcceptRangesMetadata = { Units: string list }

/// RFC 7240 preferences this resource supports.
type AcceptPreferMetadata = { Preferences: string list }

/// Preconditions this resource requires on state-changing requests.
type PreconditionRequiredMetadata = { Preconditions: Precondition list }

/// An HTTP authentication scheme this resource accepts, with the protection
/// spaces it belongs to. Realms are optional and may be empty.
type AuthSchemeMetadata = { Scheme: string; Realms: string list }
