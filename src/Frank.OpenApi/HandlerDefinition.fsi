namespace Frank.OpenApi

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

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
    static member Empty : HandlerDefinition

module HandlerDefinitionMetadata =

    val toConventions : def:HandlerDefinition -> (EndpointBuilder -> unit) list
