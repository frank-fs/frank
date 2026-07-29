namespace Frank.JsonHome

[<RequireQualifiedAccess>]
type ResourceStatus =
    | Deprecated
    | Gone

type RelMetadata = { Rel: string }

type HrefVarMetadata = { Name: string; Uri: string }

type DocsMetadata = { Uri: string }

type StatusMetadata = { Status: ResourceStatus }

[<RequireQualifiedAccess>]
type Precondition =
    | ETag
    | LastModified

type AcceptRangesMetadata = { Units: string list }

type AcceptPreferMetadata = { Preferences: string list }

type PreconditionRequiredMetadata = { Preconditions: Precondition list }

type AuthSchemeMetadata = { Scheme: string; Realms: string list }
