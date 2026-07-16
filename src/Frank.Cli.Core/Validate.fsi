module Frank.Cli.Core.Validate

open System
open Frank.Semantic

/// Outcome for a single vocab endpoint validated by frank semantic validate.
type ValidateOutcome =
    | Validated
    | LyingIri of reason: string
    | ValidateTransient of reason: string

type ValidateReport =
    { Outcomes: (string * ValidateOutcome) list }

/// Map a ValidateReport to the CLI exit code.
/// 2 if any LyingIri (durable — endpoint claims RDF IRI but doesn't serve RDF).
/// 1 if any ValidateTransient (operational, no lying-IRI).
/// 0 if all Validated.
val validateExitCode: report: ValidateReport -> int

/// Validate all Owned=true vocabulary entries by fetching them via the conneg path.
/// An endpoint serving non-RDF content when RDF is requested → LyingIri (Validated=false).
/// Returns a ValidateReport and the updated LockFile.
/// The caller is responsible for stamping integrity and writing the updated lock.
val validate:
    fetch: ConnegFetch -> now: DateTimeOffset -> lf: LockFile.LockFile -> Async<ValidateReport * LockFile.LockFile>
