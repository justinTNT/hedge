namespace Content

open Fable.Core

/// One-use, per-tab OAuth claim handoff from the Justat shell to the separately-bundled
/// blog document (unified shell, Stage 1; notes/UNIFIED-SHELL.md).
///
/// An OAuth return lands on /auth/claim in the shell (the articles document). When the
/// user came from /blog, the shell can't SPA into the blog module (it's a different
/// bundle), so it writes this handoff to sessionStorage and DOCUMENT-navigates to the
/// blog destination. The blog bundle, after its own session sync, consumes the handoff
/// to open the identity switcher pre-selected on the claimed identity, then clears it.
/// Invalid, expired, or foreign-guest handoffs are ignored (and cleared).
///
/// Shared content-client code (file-linked into each consuming Client project). Removed
/// at Stage 2 when blog folds into the shell.
module ClaimHandoff =

    [<Literal>]
    let private key = "hedge:claim-handoff"

    // Write a 5-minute, one-use handoff carrying the destination, claimed identity, and
    // the guest it applies to (so a stale handoff from a different session is rejected).
    [<Emit("sessionStorage.setItem('hedge:claim-handoff', JSON.stringify({destination:$0, identity:$1, guestId:$2, expiry:Date.now()+300000}))")>]
    let private setItem (destination: string) (identity: string) (guestId: string) : unit = jsNative

    // Read + remove atomically; return the identity to focus, or "" when absent, expired,
    // or written for a different guest than the one now signed in on this document.
    [<Emit("(function(g){try{var s=sessionStorage.getItem('hedge:claim-handoff');if(!s)return '';sessionStorage.removeItem('hedge:claim-handoff');var h=JSON.parse(s);if(!h||h.expiry<Date.now())return '';if(h.guestId&&g&&h.guestId!==g)return '';return h.identity||'';}catch(e){return '';}})($0)")>]
    let private takeItem (currentGuestId: string) : string = jsNative

    /// Write a one-use handoff before a document navigation to the blog bundle.
    let write (destination: string) (identity: string) (guestId: string) : unit =
        setItem destination identity guestId

    /// Consume the handoff after the blog bundle's session sync. Returns `Some identityId`
    /// to focus in the switcher, or `None`. Clears the handoff either way.
    let tryTake (currentGuestId: string) : string option =
        match takeItem currentGuestId with
        | "" -> None
        | identityId -> Some identityId
