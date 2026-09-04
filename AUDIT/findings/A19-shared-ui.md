---
agent_id: A19
lane: shared-ui
scope:
  - src/Frontend/YO4X.Web/src/shared/ui/Badge.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/BrandMark.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Chip.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Drawer.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/EmptyState.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Icon.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Modal.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Panel.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Skeleton.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Stars.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/StatTile.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Thumb.tsx
  - src/Frontend/YO4X.Web/src/shared/ui/Toggle.tsx
status: COMPLETE
generated: 2026-08-29T08:54:00Z
counts: { P0: 0, P1: 0, P2: 1, P3: 0 }
---

# A19 — shared-ui

## Scope audited
- `src/Frontend/YO4X.Web/src/shared/ui/Badge.tsx` (16 lines) — status pill primitive with tone classes.
- `src/Frontend/YO4X.Web/src/shared/ui/BrandMark.tsx` (15 lines) — brand wordmark and SVG logo vector.
- `src/Frontend/YO4X.Web/src/shared/ui/Chip.tsx` (27 lines) — toggle button filter pill with `aria-pressed`.
- `src/Frontend/YO4X.Web/src/shared/ui/Drawer.tsx` (69 lines) — right-edge slide-out dialog surface with `aria-modal` and dialog hook integration.
- `src/Frontend/YO4X.Web/src/shared/ui/EmptyState.tsx` (25 lines) — deliberate empty state container.
- `src/Frontend/YO4X.Web/src/shared/ui/Icon.tsx` (194 lines) — 20-member SVG icon set with static geometry and `aria-hidden` attributes.
- `src/Frontend/YO4X.Web/src/shared/ui/Modal.tsx` (149 lines) — modal surface, scrim dismiss handler, and `useDialogBehaviour` focus trap and keyboard management hook.
- `src/Frontend/YO4X.Web/src/shared/ui/Panel.tsx` (40 lines) — structured content container with conditional `aria-labelledby` region semantics.
- `src/Frontend/YO4X.Web/src/shared/ui/Skeleton.tsx` (43 lines) — loading placeholder with stacked bar generation and `aria-hidden` attributes.
- `src/Frontend/YO4X.Web/src/shared/ui/Stars.tsx` (30 lines) — star rating strip with numerical `aria-label` and rounding.
- `src/Frontend/YO4X.Web/src/shared/ui/StatTile.tsx` (33 lines) — metric card with tone-mapped delta indicators.
- `src/Frontend/YO4X.Web/src/shared/ui/Thumb.tsx` (19 lines) — strategy thumbnail placeholder tile with `aria-hidden`.
- `src/Frontend/YO4X.Web/src/shared/ui/Toggle.tsx` (28 lines) — switch toggle with native button, `role="switch"`, `aria-checked`, and keyboard trigger support.
- `src/Frontend/YO4X.Web/src/app/styles/global.css` (1,189 lines) — reviewed for overlay, scrim, dialog surface, button, and reset styling contracts.

## Verdict
The shared UI component library is well constructed and adheres strictly to accessible W3C ARIA APG standards across most primitives. `Toggle` implements a genuine `<button role="switch">` with keyboard and `aria-checked` bindings; `Chip` uses native button elements with `aria-pressed`; `Icon` uses static JSX vectors without string-based SVG interpolation; and `Panel` handles conditional `aria-labelledby` association cleanly. One defect was identified in `useDialogBehaviour` (`Modal.tsx` / `Drawer.tsx`) where reverse Tab navigation (`Shift+Tab`) from the dialog root surface container bypasses the focus trap and moves keyboard focus to background DOM elements behind the scrim.

## Findings

### [P2] Dialog focus trap allows focus to escape outside modal on Shift+Tab from dialog root surface
- **Where:** `src/Frontend/YO4X.Web/src/shared/ui/Modal.tsx:57-71`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
        const active = document.activeElement;
        if (!surface.current.contains(active)) {
          event.preventDefault();
          (event.shiftKey ? last : first).focus();
          return;
        }

        if (event.shiftKey && active === first) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && active === last) {
          event.preventDefault();
          first.focus();
        }
  ```
- **Failure:** When a modal or drawer is focused on its surface container `surface.current` (e.g. immediately upon opening if no interactive descendants exist, when non-interactive background/body text is clicked, or when `surface.current.focus()` is called), `document.activeElement` is `surface.current`. Because `surface.current.contains(surface.current)` evaluates to `true`, the `!contains(active)` check is bypassed. When the user presses `Shift+Tab`, `active === first` evaluates to `false` because `active` is the root container and `first` is the first focusable child (`modal__close`). None of the branches match, so `event.preventDefault()` is not invoked. The browser triggers native reverse Tab navigation from the surface container, escaping the modal boundary and moving keyboard focus to the background page elements behind the scrim.
- **Fix:** Update the reverse tab condition in `useDialogBehaviour` to check `if (event.shiftKey && (active === first || active === surface.current)) { event.preventDefault(); last.focus(); }`.

## Referrals
- `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:94-102` — `DashboardPage` defines a private local `StatTile` component with custom markup instead of importing `src/shared/ui/StatTile.tsx`.
- `src/Frontend/YO4X.Web/src/features/overlays/ManageAccountDrawer.tsx:204` — `ManageAccountDrawer` implements its own drawer layout and inline scrim handlers rather than reusing `src/shared/ui/Drawer.tsx`.
- `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:213` — `LinkAccountModal` duplicates modal container structure rather than reusing `src/shared/ui/Modal.tsx`.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/shared/ui/Modal.tsx:37-71` — Untested focus trap keyboard event handler branches (`Escape` key propagation, `Shift+Tab` wrapping from first element, and `Shift+Tab` from `surface.current` root container).
- `src/Frontend/YO4X.Web/src/shared/ui/Toggle.tsx:15-26` — Untested keyboard triggering (`Space`/`Enter`) and state toggling for the `role="switch"` element.
- `src/Frontend/YO4X.Web/src/shared/ui/Stars.tsx:13-28` — Untested bounds clamping branches (`rating < 0`, `rating > max`, `rating = NaN`).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 139.2s | 277170 tok | id=f5b41e66-c52f-49d6-86c4-849f93b22ce5
