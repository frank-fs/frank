namespace Frank.JsonHome

/// Compares a route template's `{name}` variables against a resource's
/// declared `hrefVar` names. Dependency-free -- shared by the FRANK003
/// compile-time analyzer (linked directly, no ProjectReference; see
/// research.md R2) and the runtime IStartupFilter check.
module HrefVarValidation =

    /// Template variables with no matching declaration, and declared names
    /// with no matching template variable.
    type Mismatch = { Missing: string list; Extra: string list }

    /// Diffs a route template's variables against a set of declared hrefVar
    /// names. A template variable repeated across multiple segments is not
    /// double-counted.
    val diff: routeTemplate: string -> declaredNames: string list -> Mismatch

    /// True when neither list has an entry.
    val isValid: mismatch: Mismatch -> bool
