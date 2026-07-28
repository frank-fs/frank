namespace Frank.Builder

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

[<AutoOpen>]
module internal MediaTypes =
    [<Literal>]
    val ApplicationJson : string = "application/json"

/// A request handler together with the endpoint metadata it contributes.
/// Metadata is an open list so that extension libraries can attach their own
/// types without Frank core knowing about them.
type HandlerDefinition =
    { Handler: RequestDelegate
      Metadata: obj list }

    static member Empty : HandlerDefinition

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HandlerDefinition =

    /// Appends a metadata object, preserving declaration order.
    val addMetadata : metadata:obj -> def:HandlerDefinition -> HandlerDefinition

    /// The first metadata entry assignable to 'T, if any.
    val tryFind<'T when 'T : not struct> : def:HandlerDefinition -> 'T option

    /// Every metadata entry assignable to 'T, in declaration order.
    val findAll<'T when 'T : not struct> : def:HandlerDefinition -> 'T list

module HandlerDefinitionMetadata =

    val toConventions : def:HandlerDefinition -> (EndpointBuilder -> unit) list
