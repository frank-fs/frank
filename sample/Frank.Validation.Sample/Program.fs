module Sample.Validation.Program

open System
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

// Same two games as Frank.Rdf.Sample/Frank.Provenance.Sample -- this sample's own addition is the
// POST endpoint and the shapes validating it, not a different domain.
let private games = dict [ "1", "Tic-tac-toe"; "2", "Connect Four" ]

// A Person shape closed to exactly name+email -- demonstrates `closed` independently of the
// recursive `node` constraint MoveAction below uses it through. `rdf:type` must be listed in
// `closed`'s ignoredProperties: per the SHACL spec, a closed shape's allowed-properties set is
// NOT implicitly extended with rdf:type, yet every node matched via `sh:targetClass` (as p1/p2
// are here) inherently carries an rdf:type triple -- omitting it here would make every
// targetClass-matched node fail ClosedConstraintComponent on rdf:type alone, closed shapes
// paired with sh:targetClass conventionally ignore rdf:type for exactly this reason.
let private personShape =
    shape (targetClass (Uri "https://schema.org/Person")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/name")) { datatype XsdDatatype.String; minCount 1; maxCount 1 }
            property (PropertyPath.Predicate(Uri "https://schema.org/email")) { datatype XsdDatatype.String; maxCount 1 }
        ]
        closed [ Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type" ]
    }

let private moveShape =
    shape (targetClass (Uri "https://schema.org/MoveAction")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/position")) {
                datatype XsdDatatype.Integer
                minCount 1
                maxCount 1
            }
            property (PropertyPath.Predicate(Uri "https://schema.org/agent")) {
                node personShape
                minCount 1
                maxCount 1
            }
        ]
    }

let private moveShapesGraph = Shacl.toShapesGraph [ moveShape; personShape ]

// Plain JSON confirmation of a move that already passed SHACL validation -- the middleware has
// already buffered/parsed/validated the body by the time this handler runs; ctx.Items carries the
// parsed graph (ValidatedGraphKey) if a handler wants it without re-parsing, though this sample's
// handler is simple enough not to need it.
let private postMove =
    fun (ctx: HttpContext) ->
        task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, _ ->
                ctx.Response.StatusCode <- 201
                do! ctx.Response.WriteAsJsonAsync({| gameId = id; accepted = true |})
            | false, _ ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({| error = $"no game with id {id}" |})
        }

let private movesResource =
    resource "/games/{id}/moves" {
        useValidation moveShapesGraph
        post postMove
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        useValidation
        resource movesResource
    }

    0
