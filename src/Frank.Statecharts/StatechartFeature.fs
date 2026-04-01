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

/// Feature for accessing hierarchical state configuration on HttpContext.
/// Set by the getCurrentStateKey closure when hierarchy is configured.
type IHierarchyFeature =
    abstract ActiveConfiguration: ActiveStateConfiguration
    abstract History: HistoryRecord

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

        member ctx.SetHierarchyFeature(config: ActiveStateConfiguration, history: HistoryRecord) =
            let feature =
                { new IHierarchyFeature with
                    member _.ActiveConfiguration = config
                    member _.History = history }

            ctx.Features.Set<IHierarchyFeature>(feature)

        member ctx.GetHierarchyFeature() : IHierarchyFeature option =
            let f = ctx.Features.Get<IHierarchyFeature>()
            if obj.ReferenceEquals(f, null) then None else Some f
