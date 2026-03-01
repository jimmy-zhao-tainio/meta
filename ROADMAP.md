# Roadmap

This is a working roadmap for keeping `isomorphic-metadata` technically coherent while the feature surface grows.

The project direction remains:

- canonical workspace on disk
- deterministic model + instance handling
- multiple equivalent representations
- model-driven construction instead of hand-maintained parallel implementations

## Current priorities

### 1. Keep command surface and docs aligned

Status: `active`

Problem:
- help text, runtime usage hints, README, and command docs can still drift
- README should stay prose, but command references must stay mechanically honest

Target:
- keep command contracts defined once where practical
- keep README curated prose, not generated command dump
- keep command examples generic unless a checked-in fixture exists to back them

Expected outcomes:
- fewer stale examples
- fewer wording mismatches
- lower maintenance cost for CLI evolution

### 2. Define the sample policy explicitly

Status: `active`

Problem:
- sample/demo content becomes expensive when it is half source, half output, and half fixture
- deleted samples should not leave ghost references behind
- examples still need a sanctioned shape

Target:
- keep examples lightweight and explicit
- when samples return, prefer plain `.cmd` entrypoints with no variables and no smart orchestration
- allow only simple companions like `setup.cmd`, `run.cmd`, and `cleanup.cmd` when needed
- avoid PowerShell-only sample flows

Expected outcomes:
- easier onboarding
- less fixture drift
- less repo clutter from demo machinery

### 3. Simplify developer binary flow

Status: `active`

Problem:
- developer convenience around `meta.exe` has been brittle
- stale shim/install paths can linger after the binary path changes

Target:
- keep one stable published binary path for development use
- avoid repo-root binary rewriting and shell shim drift
- keep PATH/setup instructions short and explicit

Expected outcomes:
- fewer local build collisions
- cleaner development workflow

### 4. Clarify and harden the load/validation boundary

Status: `future`

Problem:
- invalid instance XML currently fails during workspace load
- that is correct for the current contract, but contributors can misread where failure is supposed to occur

Target:
- keep early failure for invalid instance structure
- address the producer-side root causes that could emit invalid instance XML
- make the load boundary explicit in tests and architecture notes

Expected outcomes:
- fewer invalid-workspace edge cases
- clearer expectations for command behavior
- less confusion between parser failure and `meta check`

### 5. Expand model-driven construction of Meta itself

Status: `future`

Problem:
- parts of the project still rely on hand-maintained construction where metadata could drive structure more directly

Target:
- model more aspects of the project in metadata
- use sanctioned models to drive internal construction where that removes handwritten duplication
- treat the workspace model as one concrete example, not the endpoint

Expected outcomes:
- stronger internal consistency with the project mission
- less architectural drift between "the framework" and "what the framework says should be possible"
- better long-term leverage from the model-driven approach

## Guardrails

- invalid instance data should fail early
- no best-effort identity reconciliation
- deterministic save and generation behavior remain non-negotiable
- sanctioned models should stay on the same rails as user models whenever practical
- sample entrypoints should stay plain and explicit
- convenience layers must not replace the generic core

## Review cadence

Use this file as a short operational backlog:

- move items between `active`, `next`, and `future`
- split items when they become implementation-sized
- remove items once their architectural pressure is gone, not just when a patch exists
