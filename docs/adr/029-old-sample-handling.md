# ADR-029: Old Sample Handling

**Status:** Accepted
**Date:** 2026-05-18

## Context

The ecommerce sample now provides the realistic, end-to-end reference
application that the original `samples/RestLib.Sample` could not provide. The
original sample still has value as a small project that can be read quickly and
as the target of the existing non-ecommerce e2e suite.

Phase 10 needed a decision before changing the old sample: either retire it and
port any remaining assertions into the ecommerce suite, or keep it with a
clearer, smaller purpose.

## Decision

Keep `samples/RestLib.Sample` and reposition it as the minimal hello-world
sample.

`samples/RestLib.Sample.Ecommerce` is the canonical comprehensive reference for
how RestLib features compose in a realistic API. The old sample should no
longer try to act as a feature showcase. It should instead demonstrate the
shortest useful path for cloning the repository, running a RestLib app, and
seeing generated endpoints respond.

## Consequences

- Update the old sample README to state its minimal scope and link to the
  ecommerce sample as the full reference application.
- Keep the existing old-sample e2e suite as a regression baseline.
- Do not retire `samples/RestLib.Sample` in this cycle.
- Do not port old-sample e2e assertions into the ecommerce suite unless a
  later cleanup shows a specific duplicated assertion is no longer useful.
