namespace Frank.Statecharts

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives

/// Maps BlockReason to HTTP status codes per DD-02.
module BlockReasonMapping =
    let toStatusCode (reason: BlockReason) =
        match reason with
        | Forbidden -> 403
        | NotYourTurn -> 409
        | InvalidTransition -> 400
        | PreconditionFailed -> 412
        | Custom(code, _) -> code

    let toMessage (reason: BlockReason) =
        match reason with
        | Forbidden -> Some "Forbidden: role not authorized for this transition"
        | Custom(_, message) -> Some message
        | _ -> None

    let writeProblemResponse (ctx: HttpContext) (reason: BlockReason) =
        task {
            let status = toStatusCode reason
            ctx.Response.StatusCode <- status

            match toMessage reason with
            | Some msg ->
                ctx.Response.ContentType <- "application/problem+json"

                let title =
                    match reason with
                    | Forbidden -> "Forbidden"
                    | NotYourTurn -> "Conflict"
                    | InvalidTransition -> "Bad Request"
                    | PreconditionFailed -> "Precondition Failed"
                    | Custom _ -> "Error"

                let typeSlug =
                    match reason with
                    | Forbidden -> "forbidden"
                    | NotYourTurn -> "not-your-turn"
                    | InvalidTransition -> "invalid-transition"
                    | PreconditionFailed -> "precondition-failed"
                    | Custom _ -> "custom"

                let body =
                    sprintf
                        """{"type":"urn:frank:error:%s","title":"%s","status":%d,"detail":"%s"}"""
                        typeSlug
                        title
                        status
                        msg

                do! ctx.Response.WriteAsync(body)
            | None -> ()
        }

/// State-aware middleware that intercepts requests to stateful resources.
/// Checks endpoint metadata for StateMachineMetadata; passes through if absent.
type StateMachineMiddleware(next: RequestDelegate) =

    /// Resolve handlers for the current state using hierarchical dispatch.
    /// Reads persisted ActiveStateConfiguration from IHierarchyFeature
    /// (set by getCurrentStateKey) for LCA-based parent fallback and child override.
    static member private ResolveHandlers(meta: StateMachineMetadata, stateKey: string, ctx: HttpContext) =
        let config =
            match ctx.GetHierarchyFeature() with
            | Some f -> f.ActiveConfiguration
            | None -> ActiveStateConfiguration.empty |> ActiveStateConfiguration.add stateKey

        let resolved =
            HierarchicalRuntime.resolveHandlers meta.Hierarchy meta.StateHandlerMap config

        if List.isEmpty resolved then None else Some resolved

    /// Resolve allowed methods for the 405 Allow header using hierarchical dispatch.
    /// Reads persisted ActiveStateConfiguration from IHierarchyFeature
    /// for the full union of methods across active states and their ancestors.
    static member private ResolveAllowedMethods(meta: StateMachineMetadata, stateKey: string, ctx: HttpContext) =
        let config =
            match ctx.GetHierarchyFeature() with
            | Some f -> f.ActiveConfiguration
            | None -> ActiveStateConfiguration.empty |> ActiveStateConfiguration.add stateKey

        let methodOnlyMap = meta.StateHandlerMap |> Map.map (fun _ v -> v |> List.map fst)

        HierarchicalRuntime.resolveAllowedMethods meta.Hierarchy methodOnlyMap config
        |> Set.toList

    member _.InvokeAsync(ctx: HttpContext) : Task =
        let endpoint = ctx.GetEndpoint()

        if isNull endpoint then
            next.Invoke(ctx)
        else
            let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()

            if obj.ReferenceEquals(metadata, null) then
                next.Invoke(ctx)
            else
                StateMachineMiddleware.HandleStateful(ctx, metadata, next)

    static member private HandleStateful(ctx: HttpContext, meta: StateMachineMetadata, _next: RequestDelegate) : Task =
        task {
            let logger =
                ctx.RequestServices.GetRequiredService<ILogger<StateMachineMiddleware>>()

            let instanceId = meta.ResolveInstanceId ctx
            let httpMethod = ctx.Request.Method

            // Step 1: Look up current state from store (caches typed state in HttpContext.Items)
            let! stateKey = meta.GetCurrentStateKey ctx.RequestServices ctx instanceId

            // Step 1.5: Resolve roles for current user (skip if no roles declared)
            if not (List.isEmpty meta.Roles) then
                let resolvedRoles = meta.ResolveRoles ctx
                ctx.SetRoles(resolvedRoles)

            // Step 2: Check if HTTP method is allowed in current state.
            // Always uses hierarchical resolution (flat FSMs are auto-wrapped in __root__ XOR).
            let handlers = StateMachineMiddleware.ResolveHandlers(meta, stateKey, ctx)

            // RFC 9110: Set Allow header on all responses (mandatory on 405, useful for HATEOAS on all).
            let allowedMethods =
                StateMachineMiddleware.ResolveAllowedMethods(meta, stateKey, ctx)

            ctx.Response.Headers["Allow"] <- StringValues(String.Join(", ", allowedMethods))

            match handlers with
            | None -> ctx.Response.StatusCode <- 405
            | Some handlers ->
                let methodMatch =
                    handlers
                    |> List.tryFind (fun (m, _) -> String.Equals(m, httpMethod, StringComparison.OrdinalIgnoreCase))

                match methodMatch with
                | None -> ctx.Response.StatusCode <- 405
                | Some(_, handler) ->
                    // Step 3: Evaluate guards
                    let guardResult = meta.EvaluateGuards ctx

                    match guardResult with
                    | Blocked reason -> do! BlockReasonMapping.writeProblemResponse ctx reason
                    | Allowed ->
                        // Step 4: Invoke the state-specific handler
                        do! handler.Invoke(ctx)

                        // Step 5: Evaluate event-validation guards (post-handler)
                        let eventGuardResult = meta.EvaluateEventGuards ctx

                        match eventGuardResult with
                        | Blocked reason ->
                            if not ctx.Response.HasStarted then
                                do! BlockReasonMapping.writeProblemResponse ctx reason
                            else
                                logger.LogWarning(
                                    "Event guard blocked for instance {InstanceId} but response already started",
                                    instanceId
                                )
                        | Allowed ->
                            // Step 6: Try transition (event set by handler in HttpContext.Items)
                            let! transResult = meta.ExecuteTransition ctx.RequestServices ctx instanceId

                            match transResult with
                            | TransitionAttemptResult.NoEvent -> ()
                            | TransitionAttemptResult.Succeeded evt ->
                                // RFC 9110 Section 15.3.3: 202 should include Content-Location
                                if ctx.Response.StatusCode = 202 && not ctx.Response.HasStarted then
                                    let uri = ctx.Request.PathBase.Add(ctx.Request.Path).Value
                                    ctx.Response.Headers["Content-Location"] <- StringValues(uri)

                                for observer in meta.TransitionObservers do
                                    try
                                        observer evt
                                    with ex ->
                                        logger.LogWarning(
                                            ex,
                                            "onTransition observer threw for instance {InstanceId}",
                                            instanceId
                                        )
                            | TransitionAttemptResult.Blocked reason ->
                                if not ctx.Response.HasStarted then
                                    do! BlockReasonMapping.writeProblemResponse ctx reason
                                else
                                    logger.LogWarning(
                                        "Transition blocked for instance {InstanceId} but response already started",
                                        instanceId
                                    )
                            | TransitionAttemptResult.Invalid msg ->
                                if not ctx.Response.HasStarted then
                                    ctx.Response.StatusCode <- 400
                                    do! ctx.Response.WriteAsync(msg)
                                else
                                    logger.LogWarning(
                                        "Transition invalid for instance {InstanceId} but response already started: {Message}",
                                        instanceId,
                                        msg
                                    )
        }
        :> Task
