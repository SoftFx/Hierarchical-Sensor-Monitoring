# HSM Redesign — Design System

## 1. Color tokens
| Token | Value | Use |
|-------|-------|-----|
| `--green` | `#16a34a` | Primary action, active nav, "ok" status |
| `--green-hover` | `#15803d` | Primary button hover |
| `--blue` | `#3b82f6` | Links, informational accents |
| `--danger` | `#ef4444` | Destructive actions, "error" status |
| `--danger-bg` | `#fef2f2` | Danger button hover background |
| `--danger-border` | `#fca5a5` | Danger button hover border |
| `--border` | `#e5e7eb` | Card / list / input borders, dividers |
| `--hover` | `#f9fafb` | Row hover, list header, folder header |
| `--muted` | `#6b7280` | Secondary text, icons, captions |
| `--chip-bg` | `#f5f5f5` | Default pill background |
| `--chip-border` | `#d1d5db` | Pill / input border |
| `--navbg` | `#edf0f5` | Top navigation bar background |
| Text base | `#111827` | Primary text |
| Text secondary | `#374151` | Labels, nav items |

### Status colors (dots / chips)
- ok = `#16a34a` · warning = `#f59e0b` · error = `#ef4444` · offline/idle = `#9ca3af`

### Chip variants
- `green` bg `#f0fdf4` / border `#bbf7d0` / text `#166534`
- `blue` bg `#eff6ff` / border `#bfdbfe` / text `#1e40af`
- `gray` bg `#f3f4f6` / border `#e5e7eb` / text `#4b5563`
- `amber` bg `#fffbeb` / border `#fde68a` / text `#92400e`

### Folder accent colors (Products)
Each folder group carries a colored left border (3px) + tinted header, e.g. blue `#586bef` (tint `#eef1fb`), green `#16a34a` (tint `#f0fdf4`).

## 2. Typography
- Font stack: `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif`
- Page title (`.page-title`): 1.4rem / weight 600
- Section label (`.section-label h2`): 0.8rem, uppercase, letter-spacing .08em, muted
- Body / controls: 0.88rem
- Sub / caption (`.sub`): 0.78rem, muted
- Chip / meta: 0.72–0.8rem
- Monospace (keys, paths): `ui-monospace, SFMono-Regular, Menlo, monospace`, 0.78rem

## 3. Spacing, radius, layout
- Content container: `max-width 960px` (wide variant `1120px`), padding `2rem 1rem`, centered.
- Corner radius: cards/inputs/lists `.5rem`; avatars circle (or `.6rem` rounded square for object icons); pills `9999px`.
- Standard control padding: buttons `.45rem .85rem`; inputs `.5rem .75rem`.
- Icon action button: 36×36, 1px border, radius `.5rem`.
- Gaps: list row padding `.85rem 1rem`; toolbar gap `.5rem`.

## 4. Components
Full CSS for every component below lives in `mockups.html` (`<style>` block). Key ones:

- **Top navbar (`.rnav`)** — sticky, `--navbg`, brand + menu items; dropdown items (`.item[data-dd] > .link` toggles `.ddmenu`); active item underlined green.
- **Page header** — `.row-between` with `.page-title` left and `.actions` (buttons) right. Primary = `.btn-green`, secondary = `.btn-outline`.
- **Toolbar** — `.toolbar` with a `.search` input (right-aligned magnifier) plus optional `.select` filters.
- **List** — `.list` (bordered, rounded) → `.list-header` (count / column labels) → `.row` (hover). Row = `.cell-main` (avatar + name/sub) on the left, `.actions` on the right.
- **Avatar** — 40px circle with initials, or `.square` rounded with an icon; background tinted per item.
- **Chips / badges** — `.chip` + variant classes; used for roles, permissions, types, counts.
- **Status** — `.status` = `.dot` (colored) + label.
- **Icon actions** — `.action-btn` (36px bordered); `.danger` modifier for remove.
- **Tabs** — `.tabs` / `.tab.active` (green underline). Used on Settings and the sensor panel.
- **Form card** — `.card` with `.field` grid (label 220px + control), `.toggle` switch, `.save-bar` footer.
- **Folder accordion (Products)** — `.folder-head` (clickable) = folder icon + `.fh-title` (name with inline `.fh-edit` pencil + `.fh-count` below) + right-aligned `.fh-toggle` chevron; `.list.collapsed` hides rows and rotates the chevron. Plain icons (no bordered buttons), matching the original app.
- **Monitoring** — `.mon` two-column grid: `.panel` tree on the left, sensor `.panel` on the right with breadcrumb, big value, `.kv` meta grid, tabs and a bar `.chart`.
- **Auth card** — `.auth-card` centered, `.brand-badge` logo, stacked labeled inputs, full-width primary button.
