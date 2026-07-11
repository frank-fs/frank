# Frank Analyzer Diagnostics

Reference for diagnostics emitted by `Frank.Analyzers` (`Frank.Analyzers.UndereferenceableVocabAnalyzer`).

## FRANK002 — Undereferenceable vocabulary

**Severity**: Warning

**What it means**: A vocabulary namespace prefix used in this file is not confirmed-dereferenceable in the semantic lock (`frank/semantic-mappings.lock.json`). The namespace IRI could not be fetched and parsed as RDF when the lock was last written, or it has never been validated.

**Remediation**: Run `frank semantic validate` to attempt live reachability checks. If the vocabulary is reachable, the lock is updated and FRANK002 is suppressed on the next analysis run.

## FRANK003 — Lock file integrity failure

**Severity**: Error

**What it means**: The semantic lock file was found but its integrity check failed — it appears to have been hand-edited, truncated, or otherwise tampered with. No classification can be trusted while the lock is in this state.

**Remediation**: Regenerate the lock with `frank semantic finalize`. Do not hand-edit the lock file.

## FRANK004 — Stale vocabulary (editor only)

**Severity**: Info

**What it means**: A vocabulary namespace in the lock has not been re-validated within the SLA window (30 days for unowned namespaces, 90 days for owned). The cached result may be out of date.

**Note**: FRANK004 is emitted by the editor analyzer only. The CLI/CI analyzer path suppresses it to avoid clock-dependent CI gate failures. Staleness re-checks run on a scheduled cron (`network-recheck.yml`), not per PR.

**Remediation**: Run `frank semantic refresh` to re-validate vocabulary reachability and reset the staleness timer.

## FRANK005 — Validation nudge

**Severity**: Info

FRANK005 has two variants depending on how the vocabulary relates to the app:

**Variant A — Route path match** (`makeRouteHint`): A route path in the current file matches the namespace path for a vocabulary that has not yet been validated. Message: "A route path matches the namespace path for vocabulary `<prefix>`; run 'frank semantic validate' to confirm reachability and remove this warning." This fires when the app appears to be serving the vocabulary namespace but has not yet run `frank semantic validate` to confirm it.

**Variant B — Owned but unconfirmed** (`makeOwnershipNudge`): A vocabulary is recorded in the lock as `Owned = true` (same authority as the app's base URI) but no route in the current file covers its namespace path, and reachability has not been confirmed. Message: "Vocabulary `<prefix>` is recorded as owned but not yet confirmed reachable; run 'frank semantic validate'."

**Remediation**: Run `frank semantic validate` to confirm reachability. Once validated the FRANK005 note is removed.

## FRANK006 — Unknown vocabulary term

**Severity**: Warning

**What it means**: A CURIE string (e.g. `schema:Game`) references a term that is not present in the confirmed term set stored in the semantic lock for that vocabulary namespace. Either the term name is misspelled, or the lock's term set does not include it (run `frank semantic validate` to refresh).

**Remediation**: Verify the CURIE is spelled correctly. If the term is valid but missing from the lock, run `frank semantic validate` to re-fetch and update the term set.
