namespace Frank.Semantic

open System

type ResolvedField =
    { Name: string
      Iri: Uri option
      SeeAlso: Uri list
      ConstraintPattern: string option
      TypeName: string
      IsOptional: bool
      IsCollection: bool }

type ResolvedCase =
    { CaseName: string
      Iri: Uri
      IsNullary: bool }

// Invariant: LocalName and GenericArity are derived from FSharpType via
// parseLocalName at the single construction site (buildResource ~line 266).
// Never set them independently — if FSharpType ever changes, recompute both.
type ResolvedResource =
    { FSharpType: string
      LocalName: string
      GenericArity: int
      ClassIri: Uri option
      EquivalentClass: Uri option
      SeeAlso: Uri list
      ProvClass: ProvOClass option
      Fields: ResolvedField list
      Cases: ResolvedCase list
      UnionCaseCount: int
      Rt: Uri option }

type ResolvedModel =
    { Prefixes: Map<string, Uri>
      Using: Set<string>
      Resources: ResolvedResource list }

module ResolvedModel =

    val build: registry: VocabularyRegistry -> lock: LockFile.LockFile -> Result<ResolvedModel, string>

    /// Fills TypeName/IsOptional/IsCollection on each ResolvedField from the FCS-extracted TypeInfo.
    /// typesByName is keyed by TypeInfo.FullName (= ResolvedResource.FSharpType).
    /// Fields with no matching TypeInfo are left at defaults.
    /// Returns Error only when a matched field has an empty TypeName in the TypeInfo.
    val enrichTypes: typesByName: Map<string, TypeInfo> -> model: ResolvedModel -> Result<ResolvedModel, string>
