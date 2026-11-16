# Hadron Companion — ERP UI Usage Guidelines

**Status:** Approved ERP UI interpretation  
**Date:** 2026-09-06  
**Authority:** The supplied Hadron Character Model Sheet remains the visual source.

## Purpose

The companion gives Hadron ERP a recognizable, helpful personality without
turning an operational enterprise application into a decorative consumer app.
It explains state and offers orientation; it never replaces labels, validation,
status, help text or business actions.

## Context map

| Context | Pose | Maximum role | Interaction rule |
|---|---|---|---|
| Login | Welcome / present | Humanize entry and identify the product | Decorative; never blocks the form |
| Launchpad | Welcome / present | Greet the signed-in user and orient them | One short sentence; workspaces remain above the fold |
| Long-running MRP/report | Thinking | Reassure that calculation continues | Show only after the normal short-loading threshold |
| First-use / empty state | Guide / point | Point toward one primary next action | Text must explain why the state is empty |
| Recoverable page failure | Concerned / confused | Support a retry or return action | Never replace error code, trace ID or recovery instructions |

## Prohibited usage

- routine table loading, row expansion, inline edits and hover states;
- decorative repetition across multiple cards on one page;
- red alerts, authorization denial, financial approval or destructive actions;
- speech bubbles containing business rules generated only in the frontend;
- mascot-led navigation that hides the normal enterprise information architecture.

## Size and placement

- Login/Launchpad: 160–240 CSS pixels high on desktop.
- Full-page empty/error state: 112–160 CSS pixels high.
- Long-running progress: 72–112 CSS pixels high.
- Keep at least one spacing token between the character and interactive controls.
- Do not crop the face, hands, chest mark or shoes in a full-body asset.

## Motion and accessibility

- Prefer static WebP assets. If motion is later approved, use opacity/vertical
  movement of at most 4px and a duration of at least 1.8 seconds.
- Disable all nonessential motion when `prefers-reduced-motion` is enabled.
- Decorative instances are hidden from assistive technology.
- Meaningful instances receive localized alternative text; surrounding copy still
  contains the complete information.

## Approved initial assets

- `hadron-companion-welcome.webp`
- `hadron-companion-thinking.webp`

Both are derived from the approved model-sheet identity and optimized for web use.
Additional poses require the same review for proportions, hands, face, clothing,
logo placement and color consistency before use.

## Approved collaboration sticker assets

The following optimized transparent WebP assets are approved for explicit user-authored chat messages:

- `hadron-hello.png`
- `hadron-done.png`
- `hadron-checking.png`
- `hadron-wait.png`
- `hadron-sending.png`
- `hadron-sorry.png`
- `hadron-great.png`

They are not automatic UI feedback. The stored message contains a stable sticker code; the current UI asset and localized accessible label are resolved at render time. If an asset is missing, the interface must show the label instead of a broken image.
