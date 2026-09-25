# Native Plants / Hedge boundary refactor: integration complete

Date: 25 September 2026

The shared refactor is merged into main (`48a00e8`), followed by the review fixes
in `3efa413`. The authoritative framework plan and implementation record are now
on [main](https://github.com/justinTNT/hedge/blob/main/notes/HEDGE-boundary-refactor.md).

Native Plants has been rebased onto that main, and the app integration is now in
the normal `native-plants` branch and `~/Play/hedge-native-plants` checkout.
The typed contribution API/client and explicit admin registration are retained
in `3a91050` (formerly `87438ad`). The temporary `native-plants-boundaries` branch
tracks the same result; it is no longer a separate implementation path.

The rebase preserved main's shared code, including its review fixes, rather than
reapplying the shared session prerequisite from `53aa270`. Only that historical
commit's Native Plants `AllowGuestUploads = false` change remains app-specific.
Compared with the previous integration tree, the only code differences are
main's two reviewed changes. App schemas and migrations are unchanged.

Implemented boundaries:

- Private contribution JSON uses generated v2 contracts, codecs and clients.
- Main-app and admin navigation share credential events and confirmed capability
  loading; stale session/key responses cannot restore private state.
- Admin registration names the app's supported resources explicitly. Identity
  supports list/read only, including for the owner.

The previous PascalCase JSON endpoints remain for one compatibility release.
Multipart/media routes retain their existing paths and app-owned behavior.
See `apps/native-plants/CONTRIBUTIONS.md` and `PREVIEW.md` for the compatibility
window, validation record and deployment instructions. Deployment and retiring
old clients remain separate release actions.

Local safety refs retain both published pre-rebase histories:
`backup/native-plants-before-boundary-rebase-20260925` and
`backup/native-plants-boundaries-before-rebase-20260925`.


Post-rebase verification in the normal Native Plants checkout passed: full
repository `./test.sh`, production app build, 18 importer tests, 95 Node tests,
and private-preview validation (4,041 media references and 4,610 assets). The
existing local Worker on port 8794 serves the v2 API. No remote deployment or
data migration was performed.
