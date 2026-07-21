# HSM Server

## Notifications
* Mattermost delivery shipped — third channel alongside Telegram and Slack. Per-folder chats deliver to any combination of the three.
* Unified Chat entity: Telegram + Slack + Mattermost merged into a single per-folder Chat. LevelDB migration runs on startup; legacy entities preserved.
* Chats promoted to a top-level Configuration menu entry.
* Unbound chats are now accessible from any folder.
* Per-channel Remove buttons and Send-test Slack/Mattermost in the chat editor.
* Folder bindings shown as badges on the Chats list.
* XSS hardening: chat names double-encoded in dropdown data attributes.
* Chat edit save redirects to the Chats list; non-admin PMs no longer 401.

## Alerts
* Slack destinations appear in the Alert Template notification dropdown.
* Saved TTL alert can be demoted back to a regular condition.
* Mixed-type path templates blocked; stale template policies pruned on edit.

## Folder nodes
* New Chart tab overlays up to 20 comparable child sensors with a group/type/unit selector.

## Security
* DOMPurify upgraded 3.0.1 → 3.4.11 (HIGH XSS fixes).
* Usernames limited to 64 characters across all creation flows.
* Node Chart gated on caller's product access.

## Products
* "Download agent" action moved to the product edit page.
