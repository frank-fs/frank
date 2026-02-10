namespace Frank.OpenApi

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing

type ProducesInfo =
    { StatusCode: int
      ResponseType: Type option
      ContentTypes: string list
      Description: string option }

type AcceptsInfo =
    { RequestType: Type
      ContentTypes: string list
      IsOptional: bool }

type HandlerDefinition =
    { Handler: RequestDelegate
      Name: string option
      Summary: string option
      Description: string option
      Tags: string list
      Produces: ProducesInfo list
      Accepts: AcceptsInfo list }
    static member Empty =
        { Handler = Unchecked.defaultof<_>
          Name = None
          Summary = None
          Description = None
          Tags = []
          Produces = []
          Accepts = [] }

module HandlerDefinitionMetadata =

    let toConventions (def: HandlerDefinition) : (EndpointBuilder -> unit) list =
        [
            match def.Name with
            | Some name ->
                yield fun (b: EndpointBuilder) ->
                    b.Metadata.Add(EndpointNameMetadata(name))
            | None -> ()

            match def.Summary with
            | Some s ->
                yield fun (b: EndpointBuilder) ->
                    b.Metadata.Add(EndpointSummaryAttribute(s))
            | None -> ()

            match def.Description with
            | Some d ->
                yield fun (b: EndpointBuilder) ->
                    b.Metadata.Add(EndpointDescriptionAttribute(d))
            | None -> ()

            if not (List.isEmpty def.Tags) then
                yield fun (b: EndpointBuilder) ->
                    b.Metadata.Add(TagsAttribute(def.Tags |> List.toArray))

            for p in def.Produces do
                yield fun (b: EndpointBuilder) ->
                    let contentTypes = if List.isEmpty p.ContentTypes then [| "application/json" |] else p.ContentTypes |> Array.ofList
                    match p.ResponseType with
                    | Some t ->
                        b.Metadata.Add(ProducesResponseTypeMetadata(p.StatusCode, t, contentTypes))
                    | None ->
                        b.Metadata.Add(ProducesResponseTypeMetadata(p.StatusCode, typeof<Void>, contentTypes))

            for a in def.Accepts do
                yield fun (b: EndpointBuilder) ->
                    let contentTypes = if List.isEmpty a.ContentTypes then [| "application/json" |] else a.ContentTypes |> Array.ofList
                    b.Metadata.Add(AcceptsMetadata(contentTypes, a.RequestType, a.IsOptional))
        ]
