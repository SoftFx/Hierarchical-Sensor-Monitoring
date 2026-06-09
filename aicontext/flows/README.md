# Flow Diagrams

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

Use this folder for Mermaid sequence/state diagrams that explain non-trivial
multi-component behavior.

Good candidates:

- collector queue send/retry flow;
- server update queue processing;
- alert evaluation and notification delivery;
- storage snapshot/cache recovery;
- start/stop/dispose lifecycle involving background workers.

Keep diagrams close to behavior. Link them from the owning feature docs.
