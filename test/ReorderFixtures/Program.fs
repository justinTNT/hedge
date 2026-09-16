module ReorderFixtures.Program

// CP-A reordered-completion fixtures — deterministic assertions against the REAL content-module
// `update` functions (compiled exactly as an app composes them; see the .fsproj). Each of the
// reviewer's reproduced races (notes/UNIFIED-SHELL-review-2026-09-16.md, findings F1–F5) gets a
// bug-is-fixed case and a control (current completion still accepted). No browser: we inspect the
// returned Model + whether the returned Cmd is empty (Cmd.none = the completion was dropped / no
// side effect issued) vs non-empty (accepted, with its socket/title/editor effect). The Cmds are
// never executed, so no fetch/DOM runs.

module BT = Blog.Client.Types
module BFeed = Blog.Client.Pages.Feed
module BItem = Blog.Client.Pages.Item
module BTag = Blog.Client.Pages.TagItems
module BNew = Blog.Client.Pages.NewItem
module BApp = Blog.Client.App

module AT = Articles.Client.Types
module AFeed = Articles.Client.Pages.Feed
module AItem = Articles.Client.Pages.Item
module AApp = Articles.Client.App

let rc (s: string) = Hedge.Interface.RichContent s

// --- assertion plumbing ---
let mutable private failures : string list = []
let check (name: string) (cond: bool) =
    if not cond then failures <- name :: failures
let isDrop (cmd: 'a list) = List.isEmpty cmd   // Cmd.none -> no effect issued
let notDrop (cmd: 'a list) = not (List.isEmpty cmd)

// --- shared session ---
let private guest : Client.GuestSession.GuestSessionData =
    { GuestId = "g1"; DisplayName = "Guest"; AvatarHex = "#000000"; AvatarChar = "G"; AvatarUrl = ""; Identity = None }

// --- a non-browser host context (never reads window; SetDocTitle/Navigate are inert) ---
let private ctx : Content.HostContext =
    { BaseSegments = []; MountSegments = []; InstanceId = 0
      Navigate = (fun _ -> ()); SetDocTitle = (fun _ -> ()) }

// CP-C: the update functions take injected deps (host context + typed client). The client is
// never invoked here (we only inspect the returned model + whether the Cmd is empty), so building
// it from the browser transport is load-safe.
let private bDeps : BT.Deps = { Ctx = ctx; Api = Blog.ClientGen.createClient Client.Api.browserTransport }
let private aDeps : AT.Deps = { Ctx = ctx; Api = Articles.ClientGen.createClient Client.Api.browserTransport }
let private boom = Hedge.Http.HttpFailure (500, "boom")   // a typed API failure for the failure fixtures

// ============================================================ BLOG builders
let bFeedItem (id: string) : Blog.Api.GetFeed.FeedItem =
    { Id = id; Title = "t" + id; Slug = None; Image = None; Extract = None; OwnerComment = rc ""; Timestamp = 0 }
let bFeedResp (ids: string list) (cursor: string option) : Blog.Api.GetFeed.Response =
    { Items = ids |> List.map bFeedItem; NextCursor = cursor }
let bItemRec (id: string) : Blog.Api.SubmitItem.Item =
    { Id = id; Title = "t" + id; Slug = None; Link = None; Image = None; Extract = None
      OwnerComment = rc ""; Tags = []; Comments = []; Timestamp = 0 }
let bGetItemResp (id: string) : Blog.Api.GetItem.Response = { Item = bItemRec id }
let bSubmitCommentResp (cid: string) (itemId: string) : Blog.Api.SubmitComment.Response =
    { Comment = { Id = cid; ItemId = itemId; IdentityId = "g1"; ParentId = None; Author = "a"; Picture = ""; Content = rc "c"; Timestamp = 0 } }
let bSubmitItemResp (id: string) : Blog.Api.SubmitItem.Response = { Item = bItemRec id }
let bModel : BT.Model = BApp.emptyHosted guest

// ============================================================ ARTICLES builders
let aFeedItem (id: string) : Articles.Api.GetFeed.FeedItem =
    { Id = id; Title = "t" + id; Slug = None; Image = None; Teaser = None; Timestamp = 0 }
let aFeedResp (ids: string list) (cursor: string option) : Articles.Api.GetFeed.Response =
    { Items = ids |> List.map aFeedItem; NextCursor = cursor }
let aPostResp (id: string) : Articles.Api.GetPost.Response =
    { Post = { Id = id; Title = "t" + id; Slug = None; Image = None; Teaser = None; Body = rc "b"; Comments = []; Timestamp = 0 } }
let aSubmitCommentResp (cid: string) (postId: string) : Articles.Api.SubmitComment.Response =
    { Comment = { Id = cid; PostId = postId; IdentityId = "g1"; ParentId = None; Author = "a"; Picture = ""; Content = rc "c"; Timestamp = 0 } }
let aModel : AT.Model = AApp.emptyHosted guest

[<EntryPoint>]
let main _ =
    // ---------- 1. Blog feed: current-gen read accepted, superseded-gen read dropped (F3) ----------
    let m = { bModel with Route = []; LoadGen = 5 }
    let mOk, _ = BFeed.update bDeps (BT.GotFeed (5, Ok (bFeedResp [ "b1"; "b2" ] None))) m
    check "blog.feed.currentGen.accepts" (mOk.Feed.IsSome && mOk.Feed.Value.Items.Length = 2)
    let mStale, cStale = BFeed.update bDeps (BT.GotFeed (4, Ok (bFeedResp [ "x" ] None))) m
    check "blog.feed.staleGen.drops" (mStale.Feed.IsNone && isDrop cStale)

    // ---------- 2. Blog feed: obsolete read FAILURE dropped, current failure surfaces (F4) ----------
    let mf = { bModel with Route = []; LoadGen = 5; Error = None }
    let mFailStale, _ = BFeed.update bDeps (BT.GotFeed (4, Error boom)) mf
    check "blog.feed.staleFailure.ignored" (mFailStale.Error.IsNone)
    let mFailCur, _ = BFeed.update bDeps (BT.GotFeed (5, Error boom)) mf
    check "blog.feed.currentFailure.surfaces" (mFailCur.Error = Some boom)

    // ---------- 3. Blog item: two reads of same route distinguished by gen (F3) ----------
    let mi = { bModel with Route = [ "b1" ]; LoadGen = 7; Error = Some boom }
    let miOk, ciOk = BItem.update bDeps (BT.GotItem (7, Ok (bGetItemResp "b1"))) mi
    check "blog.item.currentGen.accepts" (miOk.CurrentItem.IsSome && notDrop ciOk)
    check "blog.item.currentGen.clearsError" (miOk.Error.IsNone)                 // F4: success clears prior error
    let miStale, ciStale = BItem.update bDeps (BT.GotItem (6, Ok (bGetItemResp "b1"))) mi
    check "blog.item.staleGen.drops" (miStale.CurrentItem.IsNone && isDrop ciStale) // F1: no connectEvents on stale

    // ---------- 4. Blog pagination: cursor + gen guard the merge (F3) ----------
    let mp = { bModel with Feed = Some (bFeedResp [ "b1" ] (Some "c1")); LoadGen = 9; FeedLoadingMore = true }
    let mMerge, _ = BFeed.update bDeps (BT.GotMoreFeed (9, Some "c1", Ok (bFeedResp [ "b2" ] None))) mp
    check "blog.more.matchingCursor.merges" (mMerge.Feed.Value.Items.Length = 2 && not mMerge.FeedLoadingMore)
    let mWrongCur, _ = BFeed.update bDeps (BT.GotMoreFeed (9, Some "cX", Ok (bFeedResp [ "zz" ] None))) mp
    check "blog.more.wrongCursor.noMerge" (mWrongCur.Feed.Value.Items.Length = 1 && not mWrongCur.FeedLoadingMore)
    let mMoreStale, _ = BFeed.update bDeps (BT.GotMoreFeed (8, Some "c1", Ok (bFeedResp [ "zz" ] None))) mp
    check "blog.more.staleGen.drops" (mMoreStale.Feed.Value.Items.Length = 1 && mMoreStale.FeedLoadingMore)

    // ---------- 5. Blog comment: late success can't clear a newer draft (F2) ----------
    let mc = { bModel with Route = [ "b1" ]; CurrentItem = Some (bGetItemResp "b1"); CommentDraft = "newer text"; DraftRev = 3 }
    let mLate, cLate = BItem.update bDeps (BT.GotSubmitComment (2, Ok (bSubmitCommentResp "cm1" "b1"))) mc
    check "blog.comment.staleRev.keepsDraft" (mLate.CommentDraft = "newer text" && isDrop cLate)
    let mClear, cClear = BItem.update bDeps (BT.GotSubmitComment (3, Ok (bSubmitCommentResp "cm1" "b1"))) mc
    check "blog.comment.currentRev.clearsDraft" (mClear.CommentDraft = "" && notDrop cClear)

    // ---------- 6. Blog /new: submit success doesn't strand the form behind a spinner (F5) ----------
    let mn = { bModel with Route = [ "new" ]; IsLoading = false; Feed = Some (bFeedResp [ "b1" ] None); LoadGen = 2 }
    let mNew, _ = BNew.update bDeps (BT.GotSubmitItem (Ok (bSubmitItemResp "b9"))) mn
    check "blog.new.noSpinner" (not mNew.IsLoading && mNew.Feed.IsNone && mNew.LoadGen = 3)

    // ---------- 7. Blog invalidateInFlight: the shell's leave-hook drops the outgoing read (F1) ----------
    let mLeaveSrc = { bModel with Route = [ "b1" ]; LoadGen = 4; IsLoading = true; FeedLoadingMore = true; TagLoadingMore = true }
    let mLeft = BApp.invalidateInFlight mLeaveSrc
    check "blog.invalidateInFlight.bumpsGenClearsFlags"
        (mLeft.LoadGen = 5 && not mLeft.IsLoading && not mLeft.FeedLoadingMore && not mLeft.TagLoadingMore)
    let mAfterLeave, cAfterLeave = BItem.update bDeps (BT.GotItem (4, Ok (bGetItemResp "b1"))) mLeft
    check "blog.invalidateInFlight.dropsLateRead" (mAfterLeave.CurrentItem.IsNone && isDrop cAfterLeave)

    // ---------- 8. Blog tag items: gen + cursor guards mirror the feed (F3) ----------
    let mt = { bModel with Route = [ "tag"; "fp" ]; TagItems = Some { Tag = "fp"; Items = [ bFeedItem "b1" ]; NextCursor = Some "c1" }; LoadGen = 3; TagLoadingMore = true }
    let mTagStale, cTagStale = BTag.update bDeps (BT.GotMoreTagItems (2, Some "c1", Ok { Tag = "fp"; Items = [ bFeedItem "zz" ]; NextCursor = None })) mt
    check "blog.tag.more.staleGen.drops" (mTagStale.TagItems.Value.Items.Length = 1 && mTagStale.TagLoadingMore && isDrop cTagStale)
    let mTagMerge, _ = BTag.update bDeps (BT.GotMoreTagItems (3, Some "c1", Ok { Tag = "fp"; Items = [ bFeedItem "b2" ]; NextCursor = None })) mt
    check "blog.tag.more.matchingCursor.merges" (mTagMerge.TagItems.Value.Items.Length = 2 && not mTagMerge.TagLoadingMore)

    // ============================================================ ARTICLES (mirrors: F1/F2/F3/F4)
    // A1. Feed gen accept/reject (F3)
    let am = { aModel with Route = []; LoadGen = 5 }
    let amOk, _ = AFeed.update aDeps (AT.GotFeed (5, Ok (aFeedResp [ "a1"; "a2" ] None))) am
    check "art.feed.currentGen.accepts" (amOk.Feed.IsSome && amOk.Feed.Value.Items.Length = 2)
    let amStale, amsC = AFeed.update aDeps (AT.GotFeed (4, Ok (aFeedResp [ "x" ] None))) am
    check "art.feed.staleGen.drops" (amStale.Feed.IsNone && isDrop amsC)

    // A2. Item gen-gate suppresses the doc-title write + socket (F1); success clears Error (F4)
    let ai = { aModel with Route = [ "a1" ]; LoadGen = 7; Error = Some boom }
    let aiOk, aiC = AItem.update aDeps (AT.GotItem (7, Ok (aPostResp "a1"))) ai
    check "art.item.currentGen.accepts" (aiOk.CurrentItem.IsSome && aiOk.Error.IsNone && notDrop aiC) // batch: SetDocTitle+connect
    let aiStale, aiSC = AItem.update aDeps (AT.GotItem (6, Ok (aPostResp "a1"))) ai
    check "art.item.staleGen.dropsNoTitle" (aiStale.CurrentItem.IsNone && isDrop aiSC)               // F1: no title/socket

    // A3. Late comment success can't clear a newer draft (F2); but the append still delivers
    let ac = { aModel with Route = [ "a1" ]; CurrentItem = Some (aPostResp "a1"); CommentDraft = "newer text"; DraftRev = 3 }
    let aLate, aLateC = AItem.update aDeps (AT.GotSubmitComment (2, Ok (aSubmitCommentResp "cm1" "a1"))) ac
    check "art.comment.staleRev.keepsDraftButAppends"
        (aLate.CommentDraft = "newer text" && aLate.CurrentItem.Value.Post.Comments.Length = 1 && isDrop aLateC)
    let aClear, aClearC = AItem.update aDeps (AT.GotSubmitComment (3, Ok (aSubmitCommentResp "cm1" "a1"))) ac
    check "art.comment.currentRev.clearsDraftAndAppends"
        (aClear.CommentDraft = "" && aClear.CurrentItem.Value.Post.Comments.Length = 1 && notDrop aClearC)

    // ---------- verdict ----------
    match failures with
    | [] ->
        printfn "reorder-fixtures: OK"
        0
    | fs ->
        printfn "reorder-fixtures: FAIL"
        fs |> List.rev |> List.iter (printfn "  - %s")
        1
