# HSM Redesign — Pages Specification

13 pages, grouped by the app's top navigation. All share the design system in `DESIGN-SYSTEM.md`.

## Monitoring

### Home (Monitoring)
Two-column layout. Left panel: Filters button + tree search, then a collapsible tree (folder → product → node → sensors) with colored status dots. Right panel: breadcrumb path, sensor status + big current value, meta grid (Type, Unit, Last update, Sensor status), tabs (History / Info / Alerts / Journal), and a bar chart with an aggregation caption. Header actions: Mute, Edit, Export.

### Dashboards (list)
Page header "Dashboards" + New dashboard. Search toolbar. List of dashboards: chart-icon avatar, name (opens the dashboard), author + description, panel-count chip, row actions Open / Edit / Remove.

### Dashboard (detail)
Header with title, author/updated caption, time-range select, Add panel, Save. 2×2 grid of panels; each panel has a title, a current-value chip, and a mini bar chart.

### Products
Header "Products" (left) with New product + New folder (right), above a single search field (search by product name only). Products are grouped into folder accordions. Folder header: folder icon, name with an inline edit (pencil) icon, product-count line below the name, and a chevron on the far right (up = expanded, down = collapsed; click toggles). Each folder group has a colored left border and tinted header. Product row: initials avatar, name (opens Edit Product), managers sub-line, status ("Updated N ago" / "No data"), row actions Edit / Move to / Remove.

### Edit Product
Breadcrumb (Products / folder / product). Header with product name + Save changes. "General" card (Name, Description, TTL, history retention). "Members" card: list of members with role chip and remove action, plus Add member.

## Alerts

### Alert Templates
Header + New template. Search + folder/type filters. List: bell-icon avatar, template name, path pattern (monospace), type + alert-count chips, affected-sensors chip, Edit / Remove.

### Alert Schedules
Header + New schedule. Search + timezone filter. List: clock-icon avatar, schedule name, recurrence sub-line, timezone chip, affected-sensors chip, Edit / Remove.

## Configuration

### Access Keys
Header + New access key. Search + product/state filters. List: key-icon avatar, key name, product + masked id + copy, permission chips, status (Active/Blocked/Expired), last-access time, actions Edit / Block(Unblock) / Remove.

### Users (reference — already live)
Header "Members" + Add member. Search + role filter. List: initials avatar, username with optional Admin chip, product-role chips, Edit / Remove. This is the canonical style all other pages follow.

### Settings
Header "Settings". Tabs: Server / Backup / Self monitoring / Telegram / Database / Agent. Each tab is one or more cards of `.field` rows (labeled inputs, toggles). Sticky Cancel / Save changes footer.

### Telegram chats
Header + Connect chat. Search + folder filter. List: telegram-icon avatar, chat name, group/scope sub-line, connection status (Connected / Awaiting), Settings / Disconnect.

## Account

### Login
Centered auth card: brand badge, "Welcome back", username + password, keep-logged-in checkbox, full-width Sign in.

### Registration
Centered auth card: brand badge, "Create account", username + password + confirm, full-width Create account.

## Navigation behaviour
Top navbar mirrors the app: Home (direct), Dashboards (dropdown lists dashboards + All dashboards), Products (direct), Alerts (dropdown), Configuration (dropdown: Access keys / Users / Telegram / Settings / API), and a right-side user menu that previews the Login / Registration screens. Clicking a menu name opens its submenu; selecting an item switches the page and highlights the active section.
