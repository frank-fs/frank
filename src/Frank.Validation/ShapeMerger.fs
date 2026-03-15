namespace Frank.Validation

open System
open VDS.RDF.Parsing

/// Merges developer-provided custom constraints into auto-derived ShaclShapes.
/// Merge runs once at startup (never per-request). All operations are additive:
/// constraints can only tighten the base shape, never weaken it.
/// Contradictions are detected eagerly and raise InvalidOperationException.
module ShapeMerger =

    // ──────────────────────────────────────────────
    // Conflict-detection helpers
    // ──────────────────────────────────────────────

    /// Parse an obj to decimal for numeric boundary comparisons.
    /// Returns None if the value cannot be parsed as decimal.
    let private tryParseDecimal (value: obj) : decimal option =
        match value with
        | null -> None
        | :? decimal as d -> Some d
        | :? int as i -> Some(decimal i)
        | :? int64 as l -> Some(decimal l)
        | :? double as d -> Some(decimal d)
        | :? float32 as f -> Some(decimal f)
        | v ->
            match Decimal.TryParse(string v) with
            | true, d -> Some d
            | _ -> None

    /// Raise InvalidOperationException with a standard conflict message.
    let private raiseConflict (propertyPath: string) (constraintName: string) (message: string) =
        raise (
            InvalidOperationException(
                sprintf "Constraint conflict on property '%s' [%s]: %s" propertyPath constraintName message
            )
        )

    // ──────────────────────────────────────────────
    // SPARQL syntax validation
    // ──────────────────────────────────────────────

    /// Validate SPARQL query syntax using dotNetRdf's SparqlQueryParser.
    /// Raises InvalidOperationException if the query string is syntactically invalid.
    let private validateSparqlSyntax (query: string) =
        let parser = SparqlQueryParser()

        try
            parser.ParseFromString(query) |> ignore
        with ex ->
            raise (InvalidOperationException(sprintf "Invalid SPARQL constraint syntax: %s" ex.Message, ex))

    // ──────────────────────────────────────────────
    // Single constraint application
    // ──────────────────────────────────────────────

    /// Apply a single ConstraintKind to a PropertyShape, returning a new (tightened) PropertyShape.
    /// Raises InvalidOperationException on direct contradictions.
    let private applyConstraint (prop: PropertyShape) (kind: ConstraintKind) : PropertyShape =
        match kind with

        | PatternConstraint regex ->
            // sh:pattern is additive: multiple sh:pattern assertions = AND semantics.
            // If the base already has a primary pattern we can just add to the additional list.
            match prop.Pattern with
            | None ->
                // No existing pattern — promote to primary.
                { prop with Pattern = Some regex }
            | Some existing when existing = regex ->
                // Exact duplicate — no change needed.
                prop
            | Some _ ->
                // Already has a primary pattern — append to additional list (AND semantics).
                if prop.AdditionalPatterns |> List.contains regex then
                    prop
                else
                    { prop with
                        AdditionalPatterns = prop.AdditionalPatterns @ [ regex ] }

        | MinInclusiveConstraint customVal ->
            let merged =
                match prop.MinInclusive with
                | None -> customVal
                | Some baseVal ->
                    // Tighten: take the larger (more restrictive) lower bound.
                    match tryParseDecimal baseVal, tryParseDecimal customVal with
                    | Some b, Some c -> if c > b then customVal else baseVal
                    | _ -> customVal // Cannot compare: use custom
            // After setting, validate against MaxInclusive if present.
            let updated = { prop with MinInclusive = Some merged }

            match updated.MaxInclusive with
            | Some maxVal ->
                match tryParseDecimal merged, tryParseDecimal maxVal with
                | Some minD, Some maxD when minD > maxD ->
                    raiseConflict
                        prop.Path
                        "sh:minInclusive vs sh:maxInclusive"
                        (sprintf "minInclusive (%O) > maxInclusive (%O)" merged maxVal)
                | _ -> ()
            | None -> ()

            updated

        | MaxInclusiveConstraint customVal ->
            let merged =
                match prop.MaxInclusive with
                | None -> customVal
                | Some baseVal ->
                    // Tighten: take the smaller (more restrictive) upper bound.
                    match tryParseDecimal baseVal, tryParseDecimal customVal with
                    | Some b, Some c -> if c < b then customVal else baseVal
                    | _ -> customVal

            let updated = { prop with MaxInclusive = Some merged }

            match updated.MinInclusive with
            | Some minVal ->
                match tryParseDecimal minVal, tryParseDecimal merged with
                | Some minD, Some maxD when minD > maxD ->
                    raiseConflict
                        prop.Path
                        "sh:minInclusive vs sh:maxInclusive"
                        (sprintf "minInclusive (%O) > maxInclusive (%O)" minVal merged)
                | _ -> ()
            | None -> ()

            updated

        | MinExclusiveConstraint customVal ->
            let merged =
                match prop.MinExclusive with
                | None -> customVal
                | Some baseVal ->
                    match tryParseDecimal baseVal, tryParseDecimal customVal with
                    | Some b, Some c -> if c > b then customVal else baseVal
                    | _ -> customVal

            let updated = { prop with MinExclusive = Some merged }

            match updated.MaxExclusive with
            | Some maxVal ->
                match tryParseDecimal merged, tryParseDecimal maxVal with
                | Some minD, Some maxD when minD >= maxD ->
                    raiseConflict
                        prop.Path
                        "sh:minExclusive vs sh:maxExclusive"
                        (sprintf "minExclusive (%O) >= maxExclusive (%O)" merged maxVal)
                | _ -> ()
            | None -> ()

            updated

        | MaxExclusiveConstraint customVal ->
            let merged =
                match prop.MaxExclusive with
                | None -> customVal
                | Some baseVal ->
                    match tryParseDecimal baseVal, tryParseDecimal customVal with
                    | Some b, Some c -> if c < b then customVal else baseVal
                    | _ -> customVal

            let updated = { prop with MaxExclusive = Some merged }

            match updated.MinExclusive with
            | Some minVal ->
                match tryParseDecimal minVal, tryParseDecimal merged with
                | Some minD, Some maxD when minD >= maxD ->
                    raiseConflict
                        prop.Path
                        "sh:minExclusive vs sh:maxExclusive"
                        (sprintf "minExclusive (%O) >= maxExclusive (%O)" minVal merged)
                | _ -> ()
            | None -> ()

            updated

        | MinLengthConstraint customLen ->
            let merged =
                match prop.MinLength with
                | None -> customLen
                | Some baseLen -> max baseLen customLen // Tighten: larger lower bound

            let updated = { prop with MinLength = Some merged }

            match updated.MaxLength with
            | Some maxLen when merged > maxLen ->
                raiseConflict
                    prop.Path
                    "sh:minLength vs sh:maxLength"
                    (sprintf "minLength (%d) > maxLength (%d)" merged maxLen)
            | _ -> ()

            updated

        | MaxLengthConstraint customLen ->
            let merged =
                match prop.MaxLength with
                | None -> customLen
                | Some baseLen -> min baseLen customLen // Tighten: smaller upper bound

            let updated = { prop with MaxLength = Some merged }

            match updated.MinLength with
            | Some minLen when minLen > merged ->
                raiseConflict
                    prop.Path
                    "sh:minLength vs sh:maxLength"
                    (sprintf "minLength (%d) > maxLength (%d)" minLen merged)
            | _ -> ()

            updated

        | InValuesConstraint customValues ->
            match prop.InValues with
            | None ->
                // No existing sh:in — adopt the custom list as-is.
                { prop with
                    InValues = Some customValues }
            | Some baseValues ->
                // Intersect: the merged set must satisfy both constraints.
                let baseSet = Set.ofList baseValues
                let customSet = Set.ofList customValues
                let intersection = Set.intersect baseSet customSet

                if Set.isEmpty intersection then
                    raiseConflict
                        prop.Path
                        "sh:in"
                        (sprintf
                            "intersection of base values [%s] and custom values [%s] is empty"
                            (String.concat ", " baseValues)
                            (String.concat ", " customValues))

                { prop with
                    InValues = Some(Set.toList intersection) }

        | SparqlConstraint _ ->
            // SPARQL constraints attach to the NodeShape, not the PropertyShape.
            // The caller (mergeShape) handles this case separately.
            prop

        | CustomShaclConstraint(predicateUri, value) ->
            let pair =
                { PredicateUri = predicateUri
                  Value = value }

            { prop with
                AdditionalConstraints = prop.AdditionalConstraints @ [ pair ] }

    // ──────────────────────────────────────────────
    // Public merge API
    // ──────────────────────────────────────────────

    /// Merge a list of CustomConstraint entries into a base ShaclShape.
    ///
    /// Rules:
    ///   - Constraints are additive only (can only tighten, never weaken).
    ///   - Each constraint is applied to the PropertyShape identified by PropertyPath.
    ///   - Referencing a non-existent property path raises InvalidOperationException.
    ///   - SPARQL constraints are collected and attached to the returned ShaclShape directly.
    ///   - Cross-constraint contradictions (empty sh:in intersection, min > max, etc.) raise
    ///     InvalidOperationException immediately.
    ///   - Returns a new, immutable ShaclShape — the base shape is never mutated.
    let mergeConstraints (baseShape: ShaclShape) (constraints: CustomConstraint list) : ShaclShape =
        if constraints.IsEmpty then
            baseShape
        else
            // Validate that all property-targeted constraints reference existing paths,
            // and separate SPARQL constraints (which target the node shape, not a property).
            let knownPaths = baseShape.Properties |> List.map (fun p -> p.Path) |> Set.ofList

            let (sparqlConstraints, propertyConstraints) =
                constraints
                |> List.partition (fun c ->
                    match c.Constraint with
                    | SparqlConstraint _ -> true
                    | _ -> false)

            // Validate all property-targeted constraints reference an existing path.
            for cc in propertyConstraints do
                if not (Set.contains cc.PropertyPath knownPaths) then
                    raise (
                        InvalidOperationException(
                            sprintf
                                "Custom constraint targets non-existent property path '%s' on shape '%O'. Known paths: [%s]"
                                cc.PropertyPath
                                baseShape.NodeShapeUri
                                (String.concat ", " (Set.toList knownPaths))
                        )
                    )

            // Validate and accumulate SPARQL constraints.
            let newSparqlConstraints =
                sparqlConstraints
                |> List.choose (fun cc ->
                    match cc.Constraint with
                    | SparqlConstraint query ->
                        validateSparqlSyntax query
                        Some { NodeSparqlConstraint.Query = query }
                    | _ -> None)

            // Apply property constraints by grouping on path and folding.
            let mergedProperties =
                baseShape.Properties
                |> List.map (fun prop ->
                    // Find all constraints targeting this property path.
                    let applicableConstraints =
                        propertyConstraints
                        |> List.filter (fun cc -> cc.PropertyPath = prop.Path)
                        |> List.map (fun cc -> cc.Constraint)

                    // Fold each constraint into the property shape.
                    List.fold applyConstraint prop applicableConstraints)

            { baseShape with
                Properties = mergedProperties
                SparqlConstraints = baseShape.SparqlConstraints @ newSparqlConstraints }
