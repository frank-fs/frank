namespace Frank.Validation

open System.Security.Claims

/// Selects the appropriate SHACL shape based on a ClaimsPrincipal's claims.
/// First-match-wins: overrides are evaluated in list order.
/// If no override matches, the base (most restrictive) shape is returned.
module ShapeResolver =

    /// Determine if a principal satisfies an override's required claims.
    let private matchOverride (principal: ClaimsPrincipal) (override': ShapeOverride) =
        let claimType, requiredValues = override'.RequiredClaim
        let required = requiredValues |> Set.ofList

        if Set.isEmpty required then
            // Empty required values = catch-all, always matches.
            true
        else
            let principalValues =
                principal.Claims
                |> Seq.filter (fun c -> c.Type = claimType)
                |> Seq.map (fun c -> c.Value)
                |> Set.ofSeq

            Set.isSubset required principalValues

    /// Select the appropriate shape for a request based on the principal's claims.
    /// Returns the first matching override's shape, or the base shape if none match.
    let resolve (config: ShapeResolverConfig) (principal: ClaimsPrincipal) : ShaclShape =
        config.Overrides
        |> List.tryFind (matchOverride principal)
        |> Option.map (fun o -> o.Shape)
        |> Option.defaultValue config.BaseShape
