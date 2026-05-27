# Yard Strategy Notes

Purpose: capture design options for adding Yard support without committing to implementation yet.

## Context
Current scaffolding and roof logic are tightly coupled to one rectangular footprint and perimeter-driven pole/beam generation. Any Yard approach should minimize risk to existing House behavior.

## Approaches Discussed

### 1) House/Yard Separation Inside One Plan
Model a single plan with per-cell semantic separation (House vs Yard), and apply current scaffolding only to House areas.

Pros:
- Unified authoring workflow.
- One file defines whole property.

Cons:
- High complexity: current scaffold code assumes one perimeter loop and opposite-edge pairing.
- Requires indoor/house boundary model, edge classification, and zone-aware filtering for openings/doors/cleanup.
- Higher regression risk for existing House builds.

Risk: High (v1).

### 2) Two Parallel VFP Files (House + Yard)
Keep House file as-is and add a second Yard file that is authored in parallel and built in sequence.

Pros:
- House behavior can remain unchanged.
- Clear isolation between House and Yard logic.
- Easier phased rollout.

Cons:
- Requires alignment discipline between two files (origin/rotation expectations).
- Need clear sequencing and undo scope decisions.
- Potential terrain-operation interference if both plans level terrain.

Risk: Medium.

### 3) Config-Generated Yard Rectangle
Create Yard procedurally from config (for example: Width, Depth, AnchorEdge, OffsetFromEdge, AlongEdgeShift).

Pros:
- Fast to implement for simple rectangular yards.
- No second file format needed.
- Deterministic, easy to tune.

Cons:
- Limited expressiveness for irregular/ornamental yards.
- Needs careful axis/edge semantics to avoid user confusion.
- Can overlap House unless validated.

Risk: Medium-Low for constrained v1.

### 4) Simplest Toggle: Separate Yard Layout Built Independently
Keep everything as-is; user chooses BuildMode (House or Yard). Yard layout is separate and user places it manually wherever desired.

Pros:
- Lowest risk and lowest implementation effort.
- Preserves existing House/scaffold behavior.
- Easy user model: choose mode, choose file, place preview manually.

Cons:
- No automatic House/Yard spatial coupling.
- Requires explicit user placement step for Yard.
- Shared undo tag may remove both unless mode tagging is added later.

Risk: Low (best v1 candidate).

## Practical Recommendation
For v1, prefer Approach 4.

Why:
- Minimal disruption to existing House logic.
- Fastest path to shipping Yard capability.
- Creates clean extension points for future evolution.

## Suggested v1 Guardrails
- BuildMode enum: House | Yard.
- Add YardPlanFile config.
- Run House path unchanged when House mode is selected.
- Run Yard placement path without scaffolding in v1.
- Keep Yard terrain leveling disabled by default (or separate opt-in).
- Add clear validation/warnings for missing plan file by selected mode.

## Follow-Up Enhancements (Later)
- Separate undo scopes via mode-specific tag (House vs Yard).
- Optional Yard-specific terrain policy.
- Optional House-linked Yard anchoring helpers.
- Optional migration from mode toggle to richer zone model if needed.

## Decision Snapshot

```
Goal: Add Yard quickly with low regression risk

                +------------------------------+
                | Need full House/Yard coupling? |
                +------------------------------+
                        | yes              | no
                        v                  v
            +-------------------+   +----------------------+
            | Approach 1 (zone) |   | Approach 4 (toggle)  |
            | high complexity   |   | low risk, fast v1    |
            +-------------------+   +----------------------+
```

## Notes
These are planning notes only. No implementation commitment implied.
