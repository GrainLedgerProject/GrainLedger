# F07 offline Capture backlog

Status: deferred until the other user flows are complete. F04–F06 currently work online in the web UI; their mutation envelopes and server idempotency checks do not by themselves provide offline capture.

Before calling F07 complete, the web UI must save F04–F06 drafts and their stable operation IDs locally while disconnected, display an ordered pending queue, retry the exact same request on reconnection, and show each accepted, rejected, or conflicting result for reconciliation. Dependencies between farmer, plot, plot season, declaration, procurement, source lot, and sample submissions must be respected. A rejected record needs an explicit correction path with a new operation ID; an uncertain result must retain its original request. Tests should cover reconnect, repeated submission, out-of-order dependencies, account/company changes, and conflict display.

No offline queue or batch synchronization UI is claimed by the current online Capture screens.
