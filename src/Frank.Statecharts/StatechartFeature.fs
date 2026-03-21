namespace Frank.Statecharts

open Microsoft.AspNetCore.Http

/// Non-generic base: readable by type-agnostic middleware (e.g. AffordanceMiddleware).
type IStatechartFeature =
    abstract StateKey: string option
    abstract InstanceId: string option

/// Generic derived: readable by typed closures in StatefulResourceBuilder.
/// Eliminates boxing — state and context are stored as their concrete types.
type IStatechartFeature<'S, 'C> =
    inherit IStatechartFeature
    abstract State: 'S option
    abstract Context: 'C option

/// Extension methods for reading/writing statechart state via HttpContext.Features.
[<AutoOpen>]
module HttpContextStatechartExtensions =
    type HttpContext with
        member ctx.GetStatechartFeature() : IStatechartFeature option =
            let f = ctx.Features.Get<IStatechartFeature>()
            if obj.ReferenceEquals(f, null) then None else Some f

        member ctx.GetStatechartFeature<'S, 'C>() : IStatechartFeature<'S, 'C> option =
            let f = ctx.Features.Get<IStatechartFeature<'S, 'C>>()
            if obj.ReferenceEquals(f, null) then None else Some f

        member ctx.SetStatechartState<'S, 'C>(stateKey: string, state: 'S, context: 'C, ?instanceId: string) =
            let feature =
                { new IStatechartFeature<'S, 'C> with
                    member _.StateKey = Some stateKey
                    member _.InstanceId = instanceId
                    member _.State = Some state
                    member _.Context = Some context }
            // Dual registration: same object, two type keys (standard ASP.NET Core pattern)
            ctx.Features.Set<IStatechartFeature>(feature)
            ctx.Features.Set<IStatechartFeature<'S, 'C>>(feature)
