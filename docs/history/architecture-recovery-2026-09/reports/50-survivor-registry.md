# Ticket 50 — Mutation Survivor Second Triage Registry

Baseline: build.yml `stryker` job, workflow_dispatch run `34771955013` on branch `ticket-50-stryker-governance` (2026-09-13), Stryker.NET 4.16.0. Artifact `stryker-mutation-report` (mutation-report.html, first artifact produced after the if:always() evidence-pipeline fix in this ticket).

Raw status counts in report.files: Killed 126 / Survived 16 / NoCoverage 111 / CompileError 16 / Ignored 236. Score 49.80% over tested mutants (below the aspirational break:60 target line — declared as such, not a CI gate).

Disposition rule (matches ticket-18 precedent): every surviving mutant is either killed by a seam-level assertion (killTest column) or registered as an LLM-detection reserve with rationale + re-challenge criteria. Result: 13 killed / 3 reserved = 16/16 dispositioned.

## Kill dispositions (13) — tests in tests/EnvManager.Engine.Tests/MutationSurvivorSecondTriageTests.cs

| mutant | location | mutator / replacement | coveredBy | kill test |
|--------|----------|-----------------------|-----------|-----------|
| #3364 | ProfileEffective.cs:53 | Negate `profile.ProfileType.Equals("global")` in IsProfileApplicable | 1 | Status_GlobalInheritsSecretlessLaunch_NotApplicable |
| #3382 | ProfileEffective.cs:66 | `>= 255` -> `> 255` name-length boundary in IsProfileApplicable | 1 | Status_VariableName255_NotApplicable |
| #3414 | ProfileEffective.cs:110 | `>= 255` -> `> 255` in RunProfilePreflight | 3 | Preflight_VariableName255_Rejected |
| #3446 | ProfileEffective.cs:166 | Linq Any() -> All() in warn-tier %VAR%-defined check | 3 | PreflightDetailed_ProfileDeclaredVarSatisfiesReference_NoWarning |
| #3469 | ProfileEffective.cs:200 | Negate global-topology guard in RunProfilePreflightDetailed | 9 | PreflightDetailed_GlobalInheritsSecretlessLaunch_Errors |
| #3491 | ProfileEffective.cs:215 | `>= 255` -> `> 255` in RunProfilePreflightDetailed | 7 | PreflightDetailed_VariableName255_Errors |
| #3505 | ProfileEffective.cs:226 | Statement removal of ValidatePathFragment call (detailed PATH loop) | 1 | PreflightDetailed_InvalidPathFragment_Errors |
| #3517 | ProfileEffective.cs:256 | Conditional-true: warn report always says "strict mode: refusing" | 2 | WarnReport_NonStrict_PrintsContinuing |
| #3524 | ProfileEffective.cs:271 | Negate visited.Contains -> inherited-secret union always empty | 13 | Preflight_InheritedSecretUndeclared_Rejected |
| #3528 | ProfileEffective.cs:279 | `parent != null` -> `parent == null` (NRE on missing parent) | 1 | Preflight_MissingParent_FailsClosedNotCrash |
| #4474 | ProtectionCommand.cs:221 | `!File.Exists` flip -> external builtin list overwritten by defaults | 19 | BuiltinProtectedVarsFile_ExternalEditNotReverted |
| #4475 | ProtectionCommand.cs:222 | Statement removal of AtomicWriteJson first-run seed | 1 | BuiltinProtectedVarsFile_SeededWhenMissing |
| #4476 | ProtectionCommand.cs:223 | `??` left operand removed -> unconditional `return defaults`, external file never read | 19 | BuiltinProtectedVarsFile_ExternalSubsetHonoredInSystemScope |

## LLM-detection reserves (3) — registered, intentionally no kill test

| mutant | location | mutator | classification | rationale | llmDetection | re-challenge |
|--------|----------|---------|----------------|-----------|--------------|--------------|
| #3462 | ProfileEffective.cs:180 | Conditional-true on `Path.IsPathRooted ? target : GetFullPath(Combine(cwd, target))` | equivalent-modulo-pathological-input | For every reachable input the two arms converge: rooted targets take the identical raw value; relative targets are resolved by File.Exists against the process cwd exactly as GetFullPath does. The only divergence is a TargetExecutable with an embedded NUL, where the original throws out of CollectPreflightWarnings while the mutant reports a warning — killing the mutant would pin crash-on-poisoned-input as spec. | { candidate: true, tool: "llm semantic review", reason: "equivalent modulo embedded-NUL target; original crashes instead of warning", registeredAt: "2026-09-13", run: "34771955013" } | re-check if the warn tier gains explicit invalid-target validation or a spec decision pins crash semantics |
| #3463 | ProfileEffective.cs:180 | Conditional-false on the same ternary | equivalent-modulo-pathological-input | Mirror of #3462: always-GetFullPath is identical for rooted inputs (Combine returns the rooted second arg, GetFullPath normalizes to it); same embedded-NUL divergence as above. | { candidate: true, tool: "llm semantic review", reason: "equivalent modulo embedded-NUL target", registeredAt: "2026-09-13", run: "34771955013" } | same as #3462 |
| #3525 | ProfileEffective.cs:272 | Statement removal of `visited.Add(profile.Name)` | equivalent (ticket-18 #4369, second sighting) | Without the visited-set the walk still converges on every finite acyclic chain (re-visits union the same names); the only divergent input is a poisoned cyclic store, where the mutant recurses to an uncatchable StackOverflowException — not assertable in-process. Same disposition as ticket-18 registry entry #4369. | { candidate: true, tool: "llm semantic review", reason: "acyclic-equivalent; cyclic divergence is uncatchable SOE", registeredAt: "2026-09-13", run: "34771955013" } | re-check if a pre-resolution cycle probe lands upstream (then the guard becomes observable) |

## Notes

- Count drift honesty: the review packet quoted 13 survivors; the dispatched baseline has 16 (the protected-vars survivor cluster #5011/#5012 was joined by a new-generation cluster #4474-#4476 in LoadBuiltinProtectedVars, and ticket-19 two-tier preflight code added mutants). All 16 are dispositioned above.
- NoCoverage (111) mutants are excluded from the tested-mutant score denominator by Stryker's own accounting and are out of triage scope (they flag absent coverage, not weak assertions).
- Per the research input for this ticket (atomcode-stryker-governance), equivalent/reserve entries carry auditable rationale and a re-challenge criterion rather than a bare ignore — matching the mutant (Ruby) ignore-with-comment + periodic re-challenge pattern and the chobbledotcom/tickets audited-registry precedent.
