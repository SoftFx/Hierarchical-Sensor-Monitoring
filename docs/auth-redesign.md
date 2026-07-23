# Login & Registration — design spec

Developer handoff for the HSM authentication pages redesign. Covers visual direction, file changes, CSS, Razor markup, states, and an integration checklist.

---

## 1. Scope

| File | Action |
|---|---|
| `src/server/HSMServer/Views/Account/Index.cshtml` | Replace markup |
| `src/server/HSMServer/Views/Account/Registration.cshtml` | Replace markup |
| `src/server/HSMServer/Views/Shared/_Layout.cshtml` | Add `<body class="@(IsAuthPage ? "auth-page" : "")">` (optional) |
| `src/server/HSMServer/wwwroot/src/css/theme.css` | Already created — verify it is imported |
| `src/server/HSMServer/wwwroot/src/css/auth.css` | **NEW** — page-specific styles |
| `src/server/HSMServer/wwwroot/src/index.js` | Add `import './css/auth.css';` after `theme.css` |

After changes run `npm run build_dev` (or `build_prod`) so the bundle picks up the new CSS.

---

## 2. Visual direction

Modern minimalism. White card on a muted page surface. Thin 0.5px borders. No gradients, no shadows except a focus ring on inputs. Generous whitespace. One primary action per page (blue Submit). Iconography from FontAwesome 6.2.1 (already in `package.json`).

```
┌─────────────────────────────────────────────────┐
│ HSM                              API · Register │  ← top nav
├─────────────────────────────────────────────────┤
│                                                 │
│              ┌───────────────────┐              │
│              │  [logo]           │              │
│              │  Sign in to HSM   │              │  ← brand block
│              │  Hierarchical…    │              │
│              │                   │              │
│              │  Username         │              │
│              │  [👤 input    ]   │              │  ← fields
│              │                   │              │
│              │  Password         │              │
│              │  [🔒 input  👁 ]  │              │
│              │                   │              │
│              │  ☑ Keep logged in │              │
│              │                   │              │
│              │  [   Sign in →  ] │              │  ← primary action
│              │                   │              │
│              │  No account? Reg. │              │  ← helper link
│              └───────────────────┘              │
│                                                 │
├─────────────────────────────────────────────────┤
│ © 2026 HSM · v2.0.317      support@hsm.dev      │  ← footer strip
└─────────────────────────────────────────────────┘
```

---

## 3. Design tokens — extend `theme.css`

If `theme.css` is already in the project, these tokens exist already. Otherwise add them. All other tokens (colors, spacing, radii) come from `theme.css`.

```css
:root {
    /* surfaces */
    --hsm-bg:            #ffffff;
    --hsm-surface:       #ffffff;
    --hsm-surface-muted: #f6f7f9;
    --hsm-surface-sunk:  #f1f2f5;

    /* text */
    --hsm-text:          #14181f;
    --hsm-text-muted:    #5b6472;
    --hsm-text-faint:    #8a93a1;

    /* borders */
    --hsm-border:        rgba(20, 24, 31, 0.08);
    --hsm-border-strong: rgba(20, 24, 31, 0.16);
    --hsm-border-focus:  rgba(55, 138, 221, 0.45);

    /* accent (interactive) */
    --hsm-accent:        #378add;
    --hsm-accent-hover:  #1f6fc0;
    --hsm-accent-soft:   #e6f1fb;
    --hsm-accent-text:   #0c447c;

    /* status */
    --hsm-error:         #e24b4a;
    --hsm-error-soft:    #fcebeb;
    --hsm-error-text:    #a32d2d;

    /* typography */
    --hsm-font-sans:     -apple-system, BlinkMacSystemFont, "Segoe UI",
                         Roboto, "Helvetica Neue", Arial, sans-serif;

    /* radii */
    --hsm-radius-sm:     4px;
    --hsm-radius-md:     8px;
    --hsm-radius-lg:     12px;
}
```

---

## 4. Page-specific styles — `auth.css`

Drop this file as-is at `wwwroot/src/css/auth.css`.

```css
/* ============================================================
   HSM auth pages — Login & Registration
   ============================================================ */

.auth-page {
    background: var(--hsm-surface-muted);
    min-height: 100vh;
}

/* Centered card stage ----------------------------------------- */

.auth-stage {
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding: 64px 16px 80px;
    min-height: calc(100vh - 48px - 38px); /* minus header & footer */
}

.auth-card {
    background: var(--hsm-surface);
    border: 0.5px solid var(--hsm-border);
    border-radius: var(--hsm-radius-lg);
    width: 100%;
    max-width: 360px;
    padding: 32px 32px 28px;
}

/* Brand block ------------------------------------------------- */

.auth-brand {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    margin-bottom: 26px;
}

.auth-brand__logo {
    width: 40px;
    height: 40px;
    border-radius: 10px;
    background: var(--hsm-accent-soft);
    color: var(--hsm-accent-text);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    font-size: 18px;
}

.auth-brand__title {
    font-size: 16px;
    font-weight: 500;
    color: var(--hsm-text);
    margin: 0;
}

.auth-brand__sub {
    font-size: 12px;
    color: var(--hsm-text-muted);
    text-align: center;
    margin: 0;
}

/* Form field -------------------------------------------------- */

.auth-field {
    margin-bottom: 16px;
}

.auth-field__label {
    display: block;
    font-size: 11px;
    color: var(--hsm-text-muted);
    margin-bottom: 6px;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    font-weight: 500;
}

.auth-input {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 9px 12px;
    border: 0.5px solid var(--hsm-border-strong);
    border-radius: var(--hsm-radius-md);
    background: var(--hsm-surface);
    transition: border-color 150ms ease, box-shadow 150ms ease;
}

.auth-input:focus-within {
    border-color: var(--hsm-accent);
    box-shadow: 0 0 0 3px var(--hsm-border-focus);
}

.auth-input__icon {
    color: var(--hsm-text-faint);
    font-size: 14px;
    flex-shrink: 0;
}

.auth-input__field {
    flex: 1;
    border: 0;
    outline: 0;
    background: transparent;
    color: var(--hsm-text);
    font-size: 14px;
    font-family: inherit;
    min-width: 0;
    padding: 0;
}

.auth-input__field::placeholder {
    color: var(--hsm-text-faint);
}

.auth-input__toggle {
    color: var(--hsm-text-faint);
    font-size: 14px;
    cursor: pointer;
    background: transparent;
    border: 0;
    padding: 0;
}

.auth-input__toggle:hover {
    color: var(--hsm-text-muted);
}

/* Password strength + hint ------------------------------------ */

.auth-pwd-strength {
    display: flex;
    gap: 3px;
    margin-top: 8px;
}

.auth-pwd-strength__seg {
    flex: 1;
    height: 3px;
    border-radius: 2px;
    background: var(--hsm-surface-muted);
    transition: background-color 150ms ease;
}

.auth-pwd-strength__seg--weak    { background: var(--hsm-error); }
.auth-pwd-strength__seg--medium  { background: #ef9f27; }
.auth-pwd-strength__seg--strong  { background: #639922; }

.auth-pwd-hint {
    font-size: 11px;
    color: var(--hsm-text-faint);
    margin-top: 6px;
    line-height: 1.4;
}

/* Checkbox row ------------------------------------------------ */

.auth-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin: 16px 0 20px;
}

.auth-checkbox {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    font-size: 13px;
    color: var(--hsm-text-muted);
    cursor: pointer;
    user-select: none;
}

.auth-checkbox__box {
    width: 16px;
    height: 16px;
    border: 0.5px solid var(--hsm-border-strong);
    border-radius: 3px;
    background: var(--hsm-surface);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    color: transparent;
    transition: background-color 150ms ease, border-color 150ms ease;
}

.auth-checkbox input[type="checkbox"] {
    position: absolute;
    opacity: 0;
    pointer-events: none;
}

.auth-checkbox input[type="checkbox"]:checked + .auth-checkbox__box {
    background: var(--hsm-accent);
    border-color: var(--hsm-accent);
    color: #fff;
}

.auth-checkbox input[type="checkbox"]:focus-visible + .auth-checkbox__box {
    box-shadow: 0 0 0 3px var(--hsm-border-focus);
}

/* Submit button ----------------------------------------------- */

.auth-submit {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    padding: 11px 14px;
    border-radius: var(--hsm-radius-md);
    background: var(--hsm-accent);
    color: #fff;
    font-size: 14px;
    font-weight: 500;
    font-family: inherit;
    width: 100%;
    border: 0;
    cursor: pointer;
    transition: background-color 150ms ease;
}

.auth-submit:hover  { background: var(--hsm-accent-hover); }
.auth-submit:active { background: var(--hsm-accent-hover); transform: translateY(0.5px); }
.auth-submit:disabled {
    background: var(--hsm-border-strong);
    color: var(--hsm-surface);
    cursor: not-allowed;
}

.auth-submit__spinner {
    width: 14px;
    height: 14px;
    border: 1.5px solid rgba(255, 255, 255, 0.4);
    border-top-color: #fff;
    border-radius: 50%;
    animation: auth-spin 700ms linear infinite;
}

@keyframes auth-spin {
    to { transform: rotate(360deg); }
}

/* Helper link + footer ---------------------------------------- */

.auth-helper {
    font-size: 12px;
    color: var(--hsm-text-muted);
    text-align: center;
    margin-top: 18px;
}

.auth-helper a {
    color: var(--hsm-accent);
    font-weight: 500;
    text-decoration: none;
}

.auth-helper a:hover {
    color: var(--hsm-accent-hover);
    text-decoration: underline;
}

.auth-footer {
    padding: 12px 24px;
    border-top: 0.5px solid var(--hsm-border);
    background: var(--hsm-surface);
    font-size: 11px;
    color: var(--hsm-text-faint);
    display: flex;
    justify-content: space-between;
}

/* Error & invite banners -------------------------------------- */

.auth-banner {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 9px 11px;
    border-radius: var(--hsm-radius-md);
    font-size: 12.5px;
    margin-bottom: 16px;
}

.auth-banner--error {
    background: var(--hsm-error-soft);
    border: 0.5px solid #f7c1c1;
    color: var(--hsm-error-text);
}

.auth-banner--info {
    background: var(--hsm-accent-soft);
    border: 0.5px solid #b5d4f4;
    color: var(--hsm-accent-text);
}

.auth-banner b { font-weight: 500; }

/* Responsive -------------------------------------------------- */

@media (max-width: 480px) {
    .auth-stage  { padding: 24px 12px 32px; }
    .auth-card   { padding: 24px 20px 20px; max-width: 100%; }
    .auth-footer { padding: 10px 12px; flex-direction: column; gap: 4px; }
}
```

---

## 5. Razor markup

### 5.1 Login — `Views/Account/Index.cshtml`

```cshtml
@using HSMServer.Controllers
@model HSMServer.Model.ViewModel.LoginViewModel
@{
    ViewData["Title"] = "Login";
    Layout = "~/Views/Shared/_Layout.cshtml";
}

<div class="auth-stage">
    <form class="auth-card"
          method="post"
          action="@nameof(AccountController.Authenticate)"
          enctype="application/x-www-form-urlencoded"
          autocomplete="on">

        <div class="auth-brand">
            <span class="auth-brand__logo" aria-hidden="true">
                <i class="fa-solid fa-heart-pulse"></i>
            </span>
            <h1 class="auth-brand__title">Sign in to HSM</h1>
            <p class="auth-brand__sub">Hierarchical Sensor Monitoring</p>
        </div>

        @if (TempData["ErrorMessage"] != null)
        {
            <div class="auth-banner auth-banner--error" role="alert">
                <i class="fa-solid fa-circle-exclamation" aria-hidden="true"></i>
                <span>@TempData["ErrorMessage"]</span>
            </div>
        }

        <div class="auth-field">
            <label class="auth-field__label" for="usernameInput">Username</label>
            <div class="auth-input">
                <i class="fa-solid fa-user auth-input__icon" aria-hidden="true"></i>
                <input class="auth-input__field"
                       id="usernameInput"
                       name="username"
                       type="text"
                       autocomplete="username"
                       value="@Model.Username"
                       placeholder="username"
                       required />
            </div>
        </div>

        <div class="auth-field">
            <label class="auth-field__label" for="passwordInput">Password</label>
            <div class="auth-input">
                <i class="fa-solid fa-lock auth-input__icon" aria-hidden="true"></i>
                <input class="auth-input__field"
                       id="passwordInput"
                       name="password"
                       type="password"
                       autocomplete="current-password"
                       value="@Model.Password"
                       placeholder="••••••••"
                       required />
                <button type="button"
                        class="auth-input__toggle"
                        aria-label="Show password"
                        data-toggle-password="passwordInput">
                    <i class="fa-solid fa-eye" aria-hidden="true"></i>
                </button>
            </div>
        </div>

        <div class="auth-row">
            <label class="auth-checkbox">
                <input type="checkbox" id="keepLoggedInInput" name="keepLoggedIn" value="true" />
                <span class="auth-checkbox__box" aria-hidden="true">
                    <i class="fa-solid fa-check" style="font-size:10px;"></i>
                </span>
                Keep me logged in
            </label>
        </div>

        <button type="submit" id="loginSubmit" class="auth-submit">
            Sign in
            <i class="fa-solid fa-arrow-right" aria-hidden="true"></i>
        </button>

        <p class="auth-helper">
            No account?
            <a href="@Url.Action("Registration", "Account")">Register here</a>
        </p>
    </form>
</div>

<footer class="auth-footer">
    <span>&copy; @DateTime.UtcNow.Year HSM &middot; v@(ServerConfig.Version)</span>
    <span>support@hsm.dev</span>
</footer>
```

### 5.2 Registration — `Views/Account/Registration.cshtml`

```cshtml
@model HSMServer.Model.ViewModel.RegistrationViewModel
@{
    ViewData["Title"] = "Registration";
    Layout = "~/Views/Shared/_Layout.cshtml";
    bool isInvite = !string.IsNullOrEmpty(Model.ProductKey);
}

<div class="auth-stage">
    <form class="auth-card"
          method="post"
          action="Registrate"
          enctype="application/x-www-form-urlencoded"
          autocomplete="on">

        <div class="auth-brand">
            <span class="auth-brand__logo" aria-hidden="true">
                <i class="fa-solid fa-user-plus"></i>
            </span>
            <h1 class="auth-brand__title">Create your account</h1>
            <p class="auth-brand__sub">
                @(isInvite ? "Finish the invitation to join the team" : "Join your team and start monitoring")
            </p>
        </div>

        @if (isInvite)
        {
            <div class="auth-banner auth-banner--info">
                <i class="fa-solid fa-envelope" aria-hidden="true"></i>
                <span>Invited as <b>@Model.Role</b></span>
            </div>
        }

        @if (TempData["ErrorMessage"] != null)
        {
            <div class="auth-banner auth-banner--error" role="alert">
                <i class="fa-solid fa-circle-exclamation" aria-hidden="true"></i>
                <span>@TempData["ErrorMessage"]</span>
            </div>
        }

        <div class="auth-field">
            <label class="auth-field__label" for="username">Username</label>
            <div class="auth-input">
                <i class="fa-solid fa-user auth-input__icon" aria-hidden="true"></i>
                <input class="auth-input__field"
                       id="username" name="username" type="text"
                       autocomplete="username"
                       value="@Model.Username"
                       placeholder="choose a username"
                       required minlength="3" />
            </div>
        </div>

        <div class="auth-field">
            <label class="auth-field__label" for="password">Password</label>
            <div class="auth-input">
                <i class="fa-solid fa-lock auth-input__icon" aria-hidden="true"></i>
                <input class="auth-input__field"
                       id="password" name="password" type="password"
                       autocomplete="new-password"
                       value="@Model.Password"
                       placeholder="at least 8 characters"
                       required minlength="8" />
                <button type="button" class="auth-input__toggle"
                        aria-label="Show password"
                        data-toggle-password="password">
                    <i class="fa-solid fa-eye" aria-hidden="true"></i>
                </button>
            </div>
            <div class="auth-pwd-strength" data-pwd-meter="password" aria-hidden="true">
                <span class="auth-pwd-strength__seg"></span>
                <span class="auth-pwd-strength__seg"></span>
                <span class="auth-pwd-strength__seg"></span>
                <span class="auth-pwd-strength__seg"></span>
            </div>
            <p class="auth-pwd-hint">Use letters, numbers, and at least one symbol.</p>
        </div>

        <div class="auth-field" style="margin-bottom:22px;">
            <label class="auth-field__label" for="secondPassword">Repeat password</label>
            <div class="auth-input">
                <i class="fa-solid fa-lock auth-input__icon" aria-hidden="true"></i>
                <input class="auth-input__field"
                       id="secondPassword" name="secondPassword" type="password"
                       autocomplete="new-password"
                       value="@Model.SecondPassword"
                       placeholder="repeat it"
                       required />
            </div>
        </div>

        <input id="productKey" name="ProductKey" value="@Model.ProductKey" type="hidden" />
        <input id="role"       name="Role"       value="@Model.Role"       type="hidden" />
        <input id="ticketId"   name="TicketId"   value="@Model.TicketId"   type="hidden" />

        <button type="submit" id="loginSubmit" class="auth-submit">
            @(isInvite ? "Accept invite" : "Create account")
            <i class="fa-solid fa-arrow-right" aria-hidden="true"></i>
        </button>

        <p class="auth-helper">
            Already have an account?
            <a href="@Url.Action("Index", "Account")">Sign in</a>
        </p>
    </form>
</div>

<footer class="auth-footer">
    <span>&copy; @DateTime.UtcNow.Year HSM &middot; v@(ServerConfig.Version)</span>
    <span>support@hsm.dev</span>
</footer>
```

---

## 6. Minimal JS — password toggle and strength meter

Add to a new file `wwwroot/src/ts/auth.ts` and import it from `index.js` after the other scripts.

```ts
// Show / hide password
document.querySelectorAll<HTMLButtonElement>('[data-toggle-password]').forEach((btn) => {
    btn.addEventListener('click', () => {
        const id = btn.dataset.togglePassword!;
        const input = document.getElementById(id) as HTMLInputElement | null;
        if (!input) return;

        const isPwd = input.type === 'password';
        input.type = isPwd ? 'text' : 'password';
        btn.setAttribute('aria-label', isPwd ? 'Hide password' : 'Show password');
        btn.querySelector('i')!.className = isPwd ? 'fa-solid fa-eye-slash' : 'fa-solid fa-eye';
    });
});

// Password strength meter (4-segment)
document.querySelectorAll<HTMLElement>('[data-pwd-meter]').forEach((meter) => {
    const id = meter.dataset.pwdMeter!;
    const input = document.getElementById(id) as HTMLInputElement | null;
    if (!input) return;

    const segs = meter.querySelectorAll<HTMLElement>('.auth-pwd-strength__seg');

    input.addEventListener('input', () => {
        const score = scorePassword(input.value);
        segs.forEach((seg, i) => {
            seg.classList.remove(
                'auth-pwd-strength__seg--weak',
                'auth-pwd-strength__seg--medium',
                'auth-pwd-strength__seg--strong'
            );
            if (i < score) {
                seg.classList.add(
                    score <= 1 ? 'auth-pwd-strength__seg--weak' :
                    score <= 2 ? 'auth-pwd-strength__seg--medium' :
                                 'auth-pwd-strength__seg--strong'
                );
            }
        });
    });
});

function scorePassword(p: string): number {
    let s = 0;
    if (p.length >= 8) s++;
    if (/[A-Z]/.test(p) && /[a-z]/.test(p)) s++;
    if (/\d/.test(p)) s++;
    if (/[^A-Za-z0-9]/.test(p)) s++;
    return s;
}
```

---

## 7. FontAwesome icon mapping

The mockups used Tabler icons; production uses FontAwesome 6.2.1 (already imported in `index.js`).

| Where | FA class |
|---|---|
| HSM brand logo | `fa-solid fa-heart-pulse` |
| Registration brand logo | `fa-solid fa-user-plus` |
| Username field | `fa-solid fa-user` |
| Password field | `fa-solid fa-lock` |
| Repeat password field | `fa-solid fa-lock` |
| Show password toggle | `fa-solid fa-eye` / `fa-eye-slash` |
| Error banner | `fa-solid fa-circle-exclamation` |
| Invite banner | `fa-solid fa-envelope` |
| Submit arrow | `fa-solid fa-arrow-right` |
| Checkbox tick | `fa-solid fa-check` |

---

## 8. States

| State | Trigger | Visual |
|---|---|---|
| Default | initial render | Subtle gray border on inputs, no banner |
| Focus | input gains focus | Blue 1.5px border + 3px outer ring (`--hsm-border-focus`) |
| Error | `TempData["ErrorMessage"]` present | Red banner above the first field with `role="alert"` |
| Loading | submit clicked, request pending | `disabled` on submit, swap text for spinner (see `.auth-submit__spinner`) |
| Invite | Registration with `ProductKey` populated | Info banner under brand block, submit label = "Accept invite" |
| Password show | toggle clicked | Input `type=text`, eye icon swaps to `fa-eye-slash` |

For the loading state add this in `auth.ts` on form submit if you want it:

```ts
form.addEventListener('submit', () => {
    submitBtn.disabled = true;
    submitBtn.innerHTML = '<span class="auth-submit__spinner"></span> Signing in…';
});
```

---

## 9. Layout changes

The auth pages should not show the application navbar (Home / Dashboards / Products / …). The current `_Layout.cshtml` already hides it for unauthenticated users via `@if (Context.User is User)`, but the wrapper `.body-content-wrapper` still applies. Two options:

- **Recommended**: add a `auth-page` class on `<body>` when the route is `Account/Index` or `Account/Registration`, and override layout there with `background: var(--hsm-surface-muted)`.
- **Alternative**: create a dedicated `_AuthLayout.cshtml` and set `Layout = "~/Views/Shared/_AuthLayout.cshtml";` in both views. This is cleaner long term — keeps the main layout untouched.

The dedicated layout is two short files:

```cshtml
@* Views/Shared/_AuthLayout.cshtml *@
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - HSM</title>
    <script src="~/dist/main.bundle.js" asp-append-version="true"></script>
</head>
<body class="auth-page">
    @RenderBody()
</body>
</html>
```

---

## 10. Accessibility checklist

- Every input has a `<label for="…">` and matching `id`.
- Error banner uses `role="alert"` so screen readers announce it on render.
- Decorative icons get `aria-hidden="true"`.
- Password toggle is a real `<button type="button">` with `aria-label` that updates between "Show password" / "Hide password".
- Focus ring is 3px wide and uses a color with ≥3:1 contrast against white — meets WCAG 2.1.
- Form submits via standard POST so it works without JS (toggle and meter degrade silently).
- Tab order: username → password → show-toggle → keep-logged-in → submit → register link.

---

## 11. QA checklist

- [ ] Login form submits with valid credentials and redirects.
- [ ] Login with wrong credentials shows the red error banner.
- [ ] Registration matches passwords; mismatch surfaces the existing `ErrorMessage`.
- [ ] Invite-link registration (URL with `productKey`, `role`, `ticketId`) shows the info banner and the "Accept invite" submit label.
- [ ] Password show/hide toggle flips input type and icon.
- [ ] Strength meter updates as user types.
- [ ] "Keep me logged in" sends `keepLoggedIn=true` when checked, otherwise omitted (or `false`).
- [ ] Tab order is logical, focus is visible on every interactive element.
- [ ] Card collapses to full width on mobile (≤480px).
- [ ] Browser autofill (saved password) works — fields use standard `autocomplete` attributes.

---

## 12. Wiring it up — build steps

```bash
cd src/server/HSMServer
npm install                # only if dependencies changed
npm run build_dev          # for local development
# or
npm run build_prod         # for production bundle
```

Then run the server. The new `main.bundle.js` will include `theme.css`, `auth.css`, and `auth.ts`.

---

## 13. What we deliberately did NOT change

- Server-side validation, authentication logic, controllers — untouched.
- `RegistrationViewModel`, `LoginViewModel` properties — kept the same; only the markup binding to them changed.
- Bootstrap is still loaded globally. We replaced Bootstrap form classes (`form-control`, `form-check-input`, `btn btn-secondary`) with the custom `auth-*` classes on these two pages only; Bootstrap continues to work everywhere else.
