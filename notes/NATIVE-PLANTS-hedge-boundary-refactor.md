# Native Plants / Hedge boundary refactor: integration handoff

Date: 25 September 2026

The complete plan now lives on the framework branch:
[HEDGE-boundary-refactor.md](https://github.com/justinTNT/hedge/blob/hedge-framework-boundaries/notes/HEDGE-boundary-refactor.md).
Local checkout: `~/Play/hedge-framework-boundaries`, branch `hedge-framework-boundaries`.

That branch starts from main at `516f2d9` and first ports the 17 shared files from
`53aa270`. Its Native Plants Worker change is already on this branch and is omitted
from the port. Main already contains the identity/grant extraction.

The framework owns request-aware GET bindings, session-aware transport, admin
credential notifications and admin operation ceilings. Native Plants owns the typed
contribution contracts, old-client compatibility, capability/navigation wiring and
explicit admin resource list. No contribution policy or database migration changes
are intended.

Prove each shared slice with Native Plants in a temporary integration checkout
before merging framework work to main. Then bring main back here and complete the
app integration. Do not maintain a second copy of the full plan in this checkout,
merge the whole app merely to unblock framework work, or reapply `53aa270` here.

The refactor itself has not started. This note records the agreed division and
points to the authoritative implementation sequence and verification criteria.
