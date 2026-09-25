module Server.ContributionOwnership

open Fable.Core
open Hedge.Workers

/// The claim and tombstone are one D1 transaction. A write begun before OAuth must test
/// the same tombstone inside its statement, so it cannot recreate abandoned guest content.
let claim (db:D1Database) guestId provider providerUserId = promise {
    if provider = "anonymous" || provider = "guest" || System.String.IsNullOrWhiteSpace providerUserId then
        failwith "A contribution claim needs a verified provider subject"
    let stmt sql args = bind (db.prepare sql) args
    let! _ = db.batch [|
        stmt "INSERT OR IGNORE INTO contribution_claims (id,guest_id,created_at) VALUES (?,?,?)"
            [|box(newId());box guestId;box(epochNow())|]
        stmt "UPDATE plant_notes SET owner_provider=?,owner_id=? WHERE owner_provider='guest' AND owner_id=?"
            [|box provider;box providerUserId;box guestId|]
        stmt "UPDATE personal_plant_photos SET owner_provider=?,owner_id=? WHERE owner_provider='guest' AND owner_id=?"
            [|box provider;box providerUserId;box guestId|]
        // Existing account choices win if this browser and the account both chose a hero.
        stmt "INSERT OR IGNORE INTO plant_view_preferences (id,owner_provider,owner_id,plant_id,hero_photo_id) SELECT 'claimed-' || id,?,?,plant_id,hero_photo_id FROM plant_view_preferences WHERE owner_provider='guest' AND owner_id=?"
            [|box provider;box providerUserId;box guestId|]
        stmt "DELETE FROM plant_view_preferences WHERE owner_provider='guest' AND owner_id=?" [|box guestId|]
    |]
    return ()
}
