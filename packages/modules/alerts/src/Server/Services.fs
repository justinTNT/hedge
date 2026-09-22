module Alerts.Services

// C3 — the capabilities the alerts module's cron logic consumes, supplied by the host. Alerts owns
// no feed of its own; to promote a curated draft it hands the host a PromotionInput and gets back
// UNEXECUTED insert statements + the new item id, which it batches together with its own
// alerts_promotions row (one atomic COMMIT). So alerts never names a content table or depends on
// blog at compile time — the host (composing alerts + its feed module) wires PromoteToFeed.

open Fable.Core
open Hedge.Workers

/// Host-supplied curator authorization outcome, mapped by the host from its access-control decision.
/// Alerts names no access-control type (stays decoupled); the host maps its RoleResult onto these
/// three cases. SetCookie is the optional guest-cookie renewal to attach to the response — carried
/// separately from the decision (a valid session may need renewal even when it lacks the role).
type CuratorAuth =
    | Allowed of setCookie: string option
    | AuthRequired of setCookie: string option    // no acceptable session / no active identity -> 401
    | Forbidden of setCookie: string option        // identified, but not a curator -> 403

/// What the host needs to promote one curated entry into its content feed. Strings are ready-to-
/// store rich-text (Extract/OwnerComment are TipTap-doc JSON — the module wraps plain snippets);
/// the host maps these onto its feed's create surface (e.g. blog's ItemCreate).
type PromotionInput =
    { Title: string
      Link: string
      Image: string option   // article og:image (Google Alerts feeds carry none); None if unavailable
      Extract: string        // TipTap-doc JSON
      OwnerComment: string   // TipTap-doc JSON
      ArticleDate: int       // the feed entry's published time → the item's article date
      Topic: string }        // tag to apply

/// The host returns UNEXECUTED statements that create the feed item, plus its new id, so the alerts
/// module can append its alerts_promotions insert and batch the lot atomically.
type FeedInsertion = {| Stmts: D1PreparedStatement[]; ItemId: string |}

type Services =
    { DB: D1Database
      NewId: unit -> string
      Now: unit -> int
      PromoteToFeed: PromotionInput -> FeedInsertion
      /// Authorize a curator write from the request (host-injected; maps its access-control decision).
      /// Takes the request at CALL time so the Services constructor stays request-free — the cron
      /// builds Services too and must never require a request or guest-signing config.
      AuthorizeCurator: WorkerRequest -> JS.Promise<CuratorAuth> }
