namespace Frank.JsonHome

[<RequireQualifiedAccess>]
type ResourceStatus =
    | Deprecated
    | Gone

type RelMetadata = { Rel: string }

type HrefVarMetadata = { Name: string; Uri: string }

type DocsMetadata = { Uri: string }

type StatusMetadata = { Status: ResourceStatus }
