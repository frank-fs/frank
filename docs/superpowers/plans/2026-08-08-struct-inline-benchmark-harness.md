# Struct/Inline Benchmark Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recover and wire up the `#437` allocation-delta harness as `test/Frank.TestSupport`, so frank-fs/frank#485's downstream child issues (per-type `[<Struct>]`/`inline` evaluation) have a measurement mechanism to point to instead of guessing.

**Architecture:** Restore `AllocationHarness.fs` verbatim from `origin/feature/v7.3.2`@`a222d19f` into a new `test/Frank.TestSupport` project, restore its Expecto proof tests into `test/Frank.Tests`, register both in the `.sln`. No production code changes — no type in `src/` gets `[<Struct>]` or `inline` in this plan.

**Tech Stack:** F# 8.0+ / net10.0, Expecto 10.*, `Microsoft.NET.Test.Sdk`, `YoloDev.Expecto.TestSdk`.

## Global Constraints

- No conversions without measurement (frank-fs/frank#485's governing rule) — this plan adds the measurement mechanism only.
- `test/Frank.TestSupport/AllocationHarness.fs` is restored **verbatim** from commit `a222d19f` — do not modify its logic in this plan; any change is a separate, reviewable decision.
- Follow existing repo test-project conventions (Expecto, `IsTestProject=true`, `GenerateProgramFile=false`, explicit `Program.fs` with `Tests.runTestsInAssemblyWithCLIArgs`).
- `.fsi` signature files are required under `src/Frank.*/` only (per project CLAUDE.md) — `test/` projects don't carry `.fsi` files today (none of the existing `test/Frank.*.Tests` projects have one); this plan follows that and adds none.

---

### Task 1: Create `test/Frank.TestSupport` with `AllocationHarness.fs`

**Files:**
- Create: `test/Frank.TestSupport/Frank.TestSupport.fsproj`
- Create: `test/Frank.TestSupport/AllocationHarness.fs`
- Modify: `Frank.sln` (add project)

**Interfaces:**
- Produces: `Frank.TestSupport.AllocationHarness.measureAllocationDeltas (warmup: unit -> unit) (request: unit -> unit) (iterations: int) : int64[]`, `.noGrowthTrend (toleranceBytes: int64) (deltas: int64[]) : bool`, `.assertNoAllocationGrowth (warmup: unit -> unit) (request: unit -> unit) (iterations: int) (toleranceBytes: int64) : unit` — consumed by Task 2's tests, and later by Phase 1+ child issues' comparative measurements (not part of this plan).

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="AllocationHarness.fs" />
  </ItemGroup>

</Project>
```

Save as `test/Frank.TestSupport/Frank.TestSupport.fsproj`.

- [ ] **Step 2: Restore the harness source verbatim**

Run: `git show a222d19f:test/Frank.TestSupport/AllocationHarness.fs > test/Frank.TestSupport/AllocationHarness.fs`

Expected: file created, content matches (verify with `git show a222d19f:test/Frank.TestSupport/AllocationHarness.fs | diff - test/Frank.TestSupport/AllocationHarness.fs` — expect no output).

- [ ] **Step 3: Register the project in the solution**

Run: `dotnet sln Frank.sln add test/Frank.TestSupport/Frank.TestSupport.fsproj`

Expected: `Project ... added to the solution.`

- [ ] **Step 4: Build the new project standalone**

Run: `dotnet build test/Frank.TestSupport/Frank.TestSupport.fsproj`

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add test/Frank.TestSupport/Frank.TestSupport.fsproj test/Frank.TestSupport/AllocationHarness.fs Frank.sln
git commit -m "feat(#485): recover #437 allocation-delta harness as Frank.TestSupport"
```

---

### Task 2: Wire harness proof tests into `Frank.Tests`

**Files:**
- Create: `test/Frank.Tests/AllocationHarnessTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj`

**Interfaces:**
- Consumes: `Frank.TestSupport.AllocationHarness.measureAllocationDeltas`, `.noGrowthTrend`, `.assertNoAllocationGrowth` (Task 1).

- [ ] **Step 1: Add the project reference and compile entry**

In `test/Frank.Tests/Frank.Tests.fsproj`, add `AllocationHarnessTests.fs` to the existing `<ItemGroup>` of `<Compile>` entries, immediately before `Program.fs`:

```xml
    <Compile Include="ContentNegotiationTests.fs" />
    <Compile Include="AllocationHarnessTests.fs" />
    <Compile Include="Program.fs" />
```

Add the project reference to the existing `<ItemGroup>` of `<ProjectReference>` entries:

```xml
  <ItemGroup>
    <ProjectReference Include="../../src/Frank/Frank.fsproj" />
    <ProjectReference Include="../Frank.TestSupport/Frank.TestSupport.fsproj" />
  </ItemGroup>
```

- [ ] **Step 2: Restore the proof tests verbatim**

Run: `git show a222d19f:test/Frank.Tests/AllocationHarnessTests.fs > test/Frank.Tests/AllocationHarnessTests.fs`

Expected: file created, matches source (verify with `diff` as in Task 1 Step 2).

- [ ] **Step 3: Run the new tests and confirm they fail before the project reference is live, then pass after**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~AllocationHarness"`

Expected: `Passed!` — 4 test cases (`known-good: constant per-call allocation passes`, `known-bad: per-call allocation that grows with call count is detected`, `measureAllocationDeltas rejects fewer than 3 iterations`, `noGrowthTrend rejects fewer than 2 measurements`).

If any test fails, do not proceed — the harness restored in Task 1 must match `a222d19f` exactly; a failing proof test here means the restore step drifted.

- [ ] **Step 4: Run the full `Frank.Tests` suite to confirm no regression**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`

Expected: all existing tests plus the 4 new ones pass; no prior test broken by the new project reference.

- [ ] **Step 5: Commit**

```bash
git add test/Frank.Tests/AllocationHarnessTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "test(#485): prove recovered allocation-delta harness against known-good/known-bad cases"
```

---

### Task 3: Confirm `benchmarks/Frank.Benchmarks` is unaffected

**Files:**
- None modified — verification only.

**Interfaces:**
- None (read-only verification task).

- [ ] **Step 1: Build the benchmarks project**

Run: `dotnet build benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj`

Expected: `Build succeeded.` — confirms the solution-level change in Task 1 (new project added to `Frank.sln`) didn't disturb the existing benchmark project's build.

- [ ] **Step 2: Smoke-run the existing benchmark suite**

Run: `dotnet run -c Release --project benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj -- --filter "*" --job short`

Expected: BenchmarkDotNet runs `NegotiationBenchmarks`'s existing cases to completion (short job, not a full run) with no errors — confirms the harness this plan adds doesn't collide with or break the existing throughput-benchmark half of the measurement story.

- [ ] **Step 3: Build the full solution**

Run: `dotnet build Frank.sln`

Expected: `Build succeeded.` across every project, confirming Task 1/2's additions are fully integrated.

No commit — this task makes no file changes.

---

## Done

- [ ] `test/Frank.TestSupport/AllocationHarness.fs` exists, matches `a222d19f` verbatim, builds standalone.
- [ ] `test/Frank.Tests/AllocationHarnessTests.fs` passes (4/4 cases).
- [ ] Full `Frank.Tests` suite passes with no regression.
- [ ] `benchmarks/Frank.Benchmarks` still builds and runs after the solution change.
- [ ] Full `Frank.sln` builds clean.

Next: file Phase 1-4 child issues under frank-fs/frank#485 via the `decompose` skill, using the spec's phased rollout section.
