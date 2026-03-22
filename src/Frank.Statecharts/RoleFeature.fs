namespace Frank.Statecharts

open Microsoft.AspNetCore.Http

/// Typed feature for resolved roles, registered on HttpContext.Features.
/// Non-generic — roles are always Set<string>.
type IRoleFeature =
    abstract Roles: Set<string>

/// Extension methods for reading/writing resolved roles via HttpContext.Features.
[<AutoOpen>]
module HttpContextRoleExtensions =
    type HttpContext with

        member ctx.GetRoles() : Set<string> =
            let f = ctx.Features.Get<IRoleFeature>()
            if obj.ReferenceEquals(f, null) then Set.empty else f.Roles

        member ctx.SetRoles(roles: Set<string>) =
            let feature =
                { new IRoleFeature with
                    member _.Roles = roles }

            ctx.Features.Set<IRoleFeature>(feature)
