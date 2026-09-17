module Program

// Exercises the shared guest-session policy (Hedge.GuestSession) with a real signing Config
// (node WebCrypto), fake DB lookups, and an injected clock/id. See the .fsproj header.

open Fable.Core
open Hedge.GuestCookie
open Hedge.GuestSession

let mutable failures = 0
let mutable checks = 0
let check name cond =
    checks <- checks + 1
    if not cond then (failures <- failures + 1; eprintfn "  FAIL %s" name)

let cfg = { Active = { KeyId = "k1"; Secret = "test-secret-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }; Audience = "usba.se"; Previous = [] }
let t0 = 1_000_000
let mutable now = t0

let mkDeps bridge eligible linked =
    { Config = cfg
      Bridge = bridge
      Secure = false
      Now = (fun () -> now)
      NewGuestId = (fun () -> "fresh-guest")
      LegacyEligible = (fun _ -> promise { return eligible })
      LegacyHasLinkedIdentity = (fun _ -> promise { return linked }) }

// token value out of a Set-Cookie header ("hedge_guest=<token>; Path=/; ...")
let tokenOf (header: string) = header.Split(';').[0].Substring("hedge_guest=".Length)

let run () = promise {
    // 1. valid signed, not due for renewal → accept, no replacement
    now <- t0
    let! tok = issue cfg t0 LifetimeSeconds "g1"
    let deps = mkDeps HardCutover false false
    let! r1 = requireGuest deps (Some tok)
    check "signed valid -> Accepted no replacement" (match r1 with Accepted a -> a.GuestId = "g1" && a.Replacement = None | _ -> false)

    // 2. valid signed but due for renewal (< 30d left) → accept + replacement
    now <- t0 + LifetimeSeconds - 1000
    let! r2 = requireGuest deps (Some tok)
    check "signed due -> Accepted + replacement" (match r2 with Accepted a -> a.GuestId = "g1" && a.Replacement.IsSome | _ -> false)

    // 3. expired signed → reject
    now <- t0 + LifetimeSeconds + 10
    let! r3 = requireGuest deps (Some tok)
    check "expired signed -> Rejected" (r3 = Rejected)

    // 4. eligible anonymous legacy under active bridge → accept + upgrade (same subject)
    now <- t0
    let legacy = "550e8400-e29b-41d4-a716-446655440000"
    let bDeps = mkDeps (Bridge (t0 + 100000)) true false
    let! r4 = requireGuest bDeps (Some legacy)
    check "eligible anon legacy (bridge) -> Accepted same subject + replacement"
        (match r4 with Accepted a -> a.GuestId = legacy && a.Replacement.IsSome | _ -> false)
    // and the replacement is a valid signed token for that subject
    match r4 with
    | Accepted a ->
        let! v = verify cfg now (Some (tokenOf a.Replacement.Value))
        check "upgrade replacement verifies to subject" (match v with Signed c -> c.GuestId = legacy | _ -> false)
    | _ -> check "upgrade replacement verifies to subject" false

    // 5. eligible LINKED legacy under bridge → reject (must re-login)
    let! r5 = requireGuest (mkDeps (Bridge (t0 + 100000)) true true) (Some legacy)
    check "eligible linked legacy -> Rejected (re-login)" (r5 = Rejected)

    // 6. ineligible legacy under bridge → reject
    let! r6 = requireGuest (mkDeps (Bridge (t0 + 100000)) false false) (Some legacy)
    check "ineligible legacy -> Rejected" (r6 = Rejected)

    // 7. legacy under hard cutover → reject
    let! r7 = requireGuest (mkDeps HardCutover true false) (Some legacy)
    check "legacy hard cutover -> Rejected" (r7 = Rejected)

    // 8. legacy after bridge window closed → reject
    let! r8 = requireGuest (mkDeps (Bridge (t0 - 1)) true false) (Some legacy)
    check "legacy past bridge -> Rejected" (r8 = Rejected)

    // 9. missing → reject (write path never creates)
    let! r9 = requireGuest deps None
    check "missing -> Rejected" (r9 = Rejected)

    // 10. resolveOrBootstrap missing → mint fresh signed guest
    let! b1 = resolveOrBootstrap deps None
    check "bootstrap missing -> fresh signed guest" (b1.IsNew && b1.GuestId = "fresh-guest" && b1.Replacement.IsSome)

    // 11. resolveOrBootstrap valid signed → existing subject, not new
    now <- t0
    let! b2 = resolveOrBootstrap deps (Some tok)
    check "bootstrap signed -> existing subject" (not b2.IsNew && b2.GuestId = "g1")

    // 12. adopt issues a signed cookie for the adopted subject
    let! adoptedHeader = adopt deps "adopted-1"
    let! av = verify cfg now (Some (tokenOf adoptedHeader))
    check "adopt -> signed cookie for subject" (match av with Signed c -> c.GuestId = "adopted-1" | _ -> false)

    // 13. cookie Secure flag
    check "cookieHeader secure" ((cookieHeader true "tok").Contains "; Secure")
    check "cookieHeader insecure" (not ((cookieHeader false "tok").Contains "; Secure"))

    if failures = 0 then printfn "guest-session: all %d checks OK" checks
    else eprintfn "guest-session: %d of %d checks FAILED" failures checks
}

run () |> Promise.start
