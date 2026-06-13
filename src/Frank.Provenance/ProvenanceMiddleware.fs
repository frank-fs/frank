module Frank.Provenance.ProvenanceMiddleware

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Semantic
open Frank.Provenance.ProvenanceStore

/// Middleware configuration.
type ProvenanceConfig =
    { Store: ProvenanceStore
      ProvClasses: Map<string, ProvOClass>
      TypeIris: Map<string, string>
      TraceEndpointBase: string }

    static member Default =
        { Store = ProvenanceStore()
          ProvClasses = Map.empty
          TypeIris = Map.empty
          TraceEndpointBase = "/prov" }

let private provBase = "http://www.w3.org/ns/prov#"

type ProvenanceMiddleware(next: RequestDelegate, config: ProvenanceConfig) =

    member _.Invoke(ctx: HttpContext) : Task =
        task {
            let activityId = Guid.NewGuid().ToString("N")
            let startTime = DateTimeOffset.UtcNow

            let principal =
                if ctx.Request.Headers.ContainsKey("Authorization") then
                    let auth = ctx.Request.Headers["Authorization"].ToString()

                    if auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) then
                        Some(auth.Substring(7))
                    else
                        Some auth
                else
                    None

            let activityUri = $"{config.TraceEndpointBase}/{activityId}"

            // Add Link header BEFORE next.Invoke so it's set before the response body starts.
            ctx.Response.Headers.Append("Link", $"<{activityUri}>; rel=\"{provBase}wasGeneratedBy\"")

            do! next.Invoke(ctx)

            let endTime = DateTimeOffset.UtcNow

            let activity: HttpActivity =
                { ActivityId = activityId
                  Method = ctx.Request.Method
                  Path = ctx.Request.Path.ToString()
                  StartTime = startTime
                  EndTime = Some endTime
                  Principal = principal
                  ProvOClass = provBase + "Activity"
                  TypeIri = None }

            config.Store.Add(activity)
        }
        :> Task
