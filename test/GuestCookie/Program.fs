module Program

// Exercises the real Hedge.GuestCookie envelope against WebCrypto (node's crypto.subtle),
// with an injected clock. See the .fsproj header.

open Fable.Core
open Hedge.GuestCookie

let mutable failures = 0
let mutable checks = 0
let check name cond =
    checks <- checks + 1
    if not cond then
        failures <- failures + 1
        eprintfn "  FAIL %s" name

let secretA = "test-secret-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"   // >=32 bytes
let secretB = "test-secret-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
let cfg = { Active = { KeyId = "k1"; Secret = secretA }; Audience = "usba.se"; Previous = [] }
let now = 1_000_000

let run () = promise {
    // 1. round-trip: issue then verify → Signed with intact claims
    let! tok = issue cfg now 3600 "guest-123"
    let! v = verify cfg (now + 10) (Some tok)
    match v with
    | Signed c -> check "round-trip claims" (c.GuestId = "guest-123" && c.Audience = "usba.se" && c.Expiry = now + 3600)
    | _ -> check "round-trip signed" false

    // 2. expiry: past its expiry → Expired
    let! vExp = verify cfg (now + 4000) (Some tok)
    check "expired" (vExp = Expired)

    // 3. tamper the mac (last char) → Invalid
    let flip (s: string) = s.[.. s.Length - 2] + (if s.[s.Length - 1] = 'a' then "b" else "a")
    let! vTamper = verify cfg (now + 10) (Some (flip tok))
    check "tampered mac -> invalid" (vTamper = Invalid)

    // 4. wrong audience → Invalid
    let! vAud = verify { cfg with Audience = "darwin.news" } (now + 10) (Some tok)
    check "wrong audience -> invalid" (vAud = Invalid)

    // 5. wrong key (same keyId absent) → Invalid
    let! vKey = verify { cfg with Active = { KeyId = "k2"; Secret = secretB } } (now + 10) (Some tok)
    check "unknown keyId -> invalid" (vKey = Invalid)

    // 6. key rotation: active=k2, previous=[k1 unretired] verifies a k1 token
    let rotated = { Active = { KeyId = "k2"; Secret = secretB }; Audience = "usba.se"; Previous = [ (cfg.Active, now + 100000) ] }
    let! vRot = verify rotated (now + 10) (Some tok)
    check "previous key verifies" (match vRot with Signed _ -> true | _ -> false)

    // 7. retired previous key → Invalid
    let! vRet = verify { rotated with Previous = [ (cfg.Active, now - 1) ] } (now + 10) (Some tok)
    check "retired key -> invalid" (vRet = Invalid)

    // 8. malformed signed-looking token never falls back to legacy → Invalid
    let! vMal = verify cfg (now + 10) (Some "v1.k1.garbage")
    check "malformed signed -> invalid (no legacy fallback)" (vMal = Invalid)
    let! vMal2 = verify cfg (now + 10) (Some "v1.k1.YQ.deadbeef")   // valid shape, bad mac
    check "signed shape bad mac -> invalid" (vMal2 = Invalid)

    // 9. legacy raw value (UUID) → Legacy (opaque)
    let uuid = "550e8400-e29b-41d4-a716-446655440000"
    let! vLeg = verify cfg (now + 10) (Some uuid)
    check "legacy raw -> Legacy" (match vLeg with Legacy s -> s = uuid | _ -> false)

    // 10. over-long legacy → Invalid (bounded)
    let! vLong = verify cfg (now + 10) (Some (String.replicate 250 "x"))
    check "over-long legacy -> invalid" (vLong = Invalid)

    // 11. missing / empty → Missing
    let! vNone = verify cfg (now + 10) None
    check "none -> Missing" (vNone = Missing)
    let! vEmpty = verify cfg (now + 10) (Some "")
    check "empty -> Missing" (vEmpty = Missing)

    // 12. renewal window
    let claims = { GuestId = "g"; IssuedAt = now; Expiry = now + 100; Audience = "usba.se" }
    check "needsRenewal within window" (needsRenewal 200 now claims)
    check "no renewal outside window" (not (needsRenewal 50 now claims))

    if failures = 0 then printfn "guest-cookie: all %d checks OK" checks
    else eprintfn "guest-cookie: %d of %d checks FAILED" failures checks
}

run () |> Promise.start
