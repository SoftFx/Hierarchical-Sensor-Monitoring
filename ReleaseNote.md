# HSM Server

## Notifications
* Connecting a Telegram chat from the chat editor now binds to the chat being edited (including a brand-new chat saved from the editor) instead of creating a separate orphan chat. Stale Telegram bindings self-heal on reconnect, and when a Telegram chat is already bound elsewhere, the owning chat is named in the refusal message.
* The chat editor shows a "Telegram chat connected" toast and auto-refreshes to the bound state once the bot completes `/start` — the binding is async (handled by the bot), so the page previously required a manual refresh.
