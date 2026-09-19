# Frank — Agent Guidelines

Full agent guidelines: read **AGENTS.md** first — it covers behavior, code discipline, conventions, and workflow.

Summary (details in AGENTS.md):

- **Karpathy:** think before coding, surface assumptions; simplest thing that works; surgical diffs; verifiable goals.
- **Holzmann (JPL Power of Ten):** bounded loops; small single-job functions; no hidden/global mutable state; no hot-path allocation; unit-test everything.
- **F#:** `Resource` CE is the central API; every `.fs` pairs with a `.fsi` signature; preconditions via `invalidArg`/`invalidOp`/`failwith`.
- Build/test/format before done: `dotnet build Frank.sln -c Release --no-restore`, `dotnet test test/<Pkg>.Tests`, `dotnet fantomas --check`.