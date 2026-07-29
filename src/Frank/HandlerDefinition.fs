namespace Frank.Builder

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

[<AutoOpen>]
module internal MediaTypes =
    [<Literal>]
    let ApplicationJson = "application/json"

type HandlerDefinition =
    { Handler: RequestDelegate
      Metadata: obj list }

    static member Empty =
        { Handler = Unchecked.defaultof<_>
          Metadata = [] }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HandlerDefinition =

    let addMetadata (metadata: obj) (def: HandlerDefinition) =
        { def with
            Metadata = def.Metadata @ [ metadata ] }

    let tryFind<'T when 'T: not struct> (def: HandlerDefinition) : 'T option =
        def.Metadata
        |> List.tryPick (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

    let findAll<'T when 'T: not struct> (def: HandlerDefinition) : 'T list =
        def.Metadata
        |> List.choose (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

module HandlerDefinitionMetadata =

    let toConventions (def: HandlerDefinition) : (EndpointBuilder -> unit) list =
        def.Metadata
        |> List.map (fun m -> fun (b: EndpointBuilder) -> b.Metadata.Add m)
