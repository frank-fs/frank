namespace Frank

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// RFC 9457 Problem Details for HTTP APIs.
module ProblemJson =

    /// Write an RFC 9457 problem+json response to ctx.
    /// If typeUri is "about:blank" the title SHOULD be the status reason phrase.
    val write: ctx: HttpContext -> status: int -> typeUri: string -> title: string -> detail: string -> Task
