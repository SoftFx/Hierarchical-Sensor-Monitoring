# HSM — Redesign Package

Package for continuing the HSM (Hierarchical Sensor Monitoring) UI redesign.

## Contents
- `mockups.html` — interactive preview of all redesigned pages (open in a browser). Top navbar with dropdown menus mirrors the real app navigation. Use the menu to switch pages.
- `DESIGN-SYSTEM.md` — design tokens (colors, typography, spacing, radii) and the component library with ready CSS.
- `PAGES.md` — page-by-page breakdown of every screen in the mockup, with its elements, states and behaviour.

## Status
This is a **preview/mockup** for stakeholder approval. No production `.cshtml` views have been changed yet. The reference page already live in the product is **Users** (Configuration → Users); every other page here is modelled on its style.

## Design language (one line)
Clean card-list UI: centered container, bordered lists with a header row and hover rows, circular/rounded avatars, pill badges, a green primary action, subtle icon actions, and Bootstrap-style dropdowns in the top nav.

## How to reuse
1. Open `mockups.html` to review the look and navigation.
2. Copy tokens and component CSS from `DESIGN-SYSTEM.md` as the base style layer.
3. Use `PAGES.md` as the spec when rebuilding each page.
