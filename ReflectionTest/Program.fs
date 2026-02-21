module ReflectionTest

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Models.Domain
open Models.Api
open Hedge.Schema
open Hedge.Interface
open Hedge.Codec
open Hedge.Validate

/// Get the TypeInfo for a type via Fable's compiled _$reflection() function.
/// In Fable, typeof<T> compiles to T_$reflection() which returns a TypeInfo object.

[<Emit("$0.fullname")>]
let fullname (typeInfo: System.Type) : string = jsNative

[<Emit("$0.generics || []")>]
let generics (typeInfo: System.Type) : System.Type array = jsNative

[<Emit("$0.fields ? $0.fields() : []")>]
let fields (typeInfo: System.Type) : (string * System.Type) array = jsNative

[<Emit("$0.cases ? $0.cases() : []")>]
let cases (typeInfo: System.Type) : obj array = jsNative

/// Walk a record type and print its field names + type fullnames.
let inspectRecord (name: string) (t: System.Type) =
    printfn ""
    printfn "=== %s ===" name
    printfn "  fullname: %s" (fullname t)
    let fs = fields t
    for (fieldName, fieldType) in fs do
        let fn = fullname fieldType
        let gs = generics fieldType
        let genStr =
            if gs.Length > 0 then
                sprintf " [%s]" (gs |> Array.map fullname |> String.concat ", ")
            else ""
        printfn "  %-20s : %s%s" fieldName fn genStr

/// Derive a FieldAttr from a TypeInfo fullname.
let classifyField (fieldName: string) (fieldType: System.Type) =
    let fn = fullname fieldType
    let gs = generics fieldType
    // Peel option wrapper if present
    let innerFn, innerGs, isOption =
        if fn = "Microsoft.FSharp.Core.FSharpOption`1" && gs.Length = 1 then
            fullname gs.[0], generics gs.[0], true
        else
            fn, gs, false

    let attr =
        if innerFn.Contains("PrimaryKey") then Some "PrimaryKey"
        elif innerFn.Contains("MultiTenant") then Some "MultiTenant"
        elif innerFn.Contains("CreateTimestamp") then Some "CreateTimestamp"
        elif innerFn.Contains("UpdateTimestamp") then Some "UpdateTimestamp"
        elif innerFn.Contains("SoftDelete") then Some "SoftDelete"
        elif innerFn.Contains("ForeignKey") then
            let table =
                if innerGs.Length > 0 then fullname innerGs.[0]
                else "?"
            Some (sprintf "ForeignKey(%s)" table)
        elif innerFn.Contains("RichContent") then Some "RichContent"
        elif innerFn.Contains("Interface.Link") then Some "Link"
        else None

    fieldName, attr, isOption

[<EntryPoint>]
let main _ =
    printfn "Wrapper Type Reflection Test"
    printfn "============================"

    // 1. Raw inspection — see exactly what Fable emits
    inspectRecord "MicroblogItem" typeof<MicroblogItem>
    inspectRecord "ItemComment" typeof<ItemComment>
    inspectRecord "Guest" typeof<Guest>

    // 2. Classification — prove we can derive FieldAttr from TypeInfo
    printfn ""
    printfn "=== Classification ==="
    let fs = fields typeof<MicroblogItem>
    for (fieldName, fieldType) in fs do
        let name, attr, isOpt = classifyField fieldName fieldType
        let optStr = if isOpt then " option" else ""
        match attr with
        | Some a -> printfn "  %-20s -> %s%s" name a optStr
        | None -> printfn "  %-20s -> (plain)%s" name optStr

    // 3. Derive full schema via Schema.fs
    printfn ""
    printfn "=== Schema.deriveSchema ==="
    let schema = deriveSchema "MicroblogItem" typeof<MicroblogItem>
    for f in schema.Fields do
        let attrs = f.Attrs |> List.map showAttr |> String.concat ", "
        printfn "  %-20s : %-20s [%s]" f.Name (showFieldType f.Type) attrs

    // 4. Verify key expectations
    printfn ""
    let mutable pass = true

    let check desc test =
        if not test then
            printfn "FAIL: %s" desc
            pass <- false

    // Check PrimaryKey on Id
    check "MicroblogItem.Id has PrimaryKey" (
        schema.Fields |> List.exists (fun f ->
            f.Name = "Id" && f.Attrs |> List.contains FieldAttr.PrimaryKey))

    // Check MultiTenant on Host
    check "MicroblogItem.Host has MultiTenant" (
        schema.Fields |> List.exists (fun f ->
            f.Name = "Host" && f.Attrs |> List.contains FieldAttr.MultiTenant))

    // Check CreateTimestamp on CreatedAt
    check "MicroblogItem.CreatedAt has CreateTimestamp" (
        schema.Fields |> List.exists (fun f ->
            f.Name = "CreatedAt" && f.Attrs |> List.contains FieldAttr.CreateTimestamp))

    // Check ForeignKey on ItemComment.ItemId
    let commentSchema = deriveSchema "ItemComment" typeof<ItemComment>
    check "ItemComment.ItemId has ForeignKey" (
        commentSchema.Fields |> List.exists (fun f ->
            f.Name = "ItemId" && f.Attrs |> List.exists (function FieldAttr.ForeignKey _ -> true | _ -> false)))

    // Check RichContent on OwnerComment
    check "MicroblogItem.OwnerComment has RichContent" (
        schema.Fields |> List.exists (fun f ->
            f.Name = "OwnerComment" && f.Attrs |> List.contains FieldAttr.RichContent))

    // Check option fields detected
    check "MicroblogItem.DeletedAt is option" (
        schema.Fields |> List.exists (fun f ->
            f.Name = "DeletedAt" && f.Type = FOption FInt))

    // Check plain fields have no attrs
    check "MicroblogItem.Title is plain string" (
        schema.Fields |> List.exists (fun f ->
            f.Name = "Title" && f.Attrs = [] && f.Type = FString))

    // ============================================================
    // 5. Codec round-trip tests
    // ============================================================
    printfn ""
    printfn "=== Codec Round-Trip ==="

    // -- Guest --
    let testGuest : Guest = {
        Id = PrimaryKey "guest-1"
        Host = MultiTenant "example.com"
        Name = "Alice"
        Picture = "https://example.com/alice.png"
        SessionId = "sess-abc"
        CreatedAt = CreateTimestamp 1700000000
        DeletedAt = Some (SoftDelete 1700001000)
    }

    let guestJson = encode testGuest |> Encode.toString 0
    printfn "  Guest JSON: %s" guestJson

    match Decode.fromString (decode<Guest>()) guestJson with
    | Error err ->
        printfn "FAIL: Guest decode error: %s" err
        pass <- false
    | Ok decoded ->
        let (PrimaryKey id) = decoded.Id
        let (MultiTenant host) = decoded.Host
        let (CreateTimestamp cat) = decoded.CreatedAt
        check "Guest.Id round-trips" (id = "guest-1")
        check "Guest.Host round-trips" (host = "example.com")
        check "Guest.Name round-trips" (decoded.Name = "Alice")
        check "Guest.Picture round-trips" (decoded.Picture = "https://example.com/alice.png")
        check "Guest.SessionId round-trips" (decoded.SessionId = "sess-abc")
        check "Guest.CreatedAt round-trips" (cat = 1700000000)
        check "Guest.DeletedAt round-trips" (
            match decoded.DeletedAt with
            | Some (SoftDelete v) -> v = 1700001000
            | None -> false)
        printfn "  Guest round-trip: OK"

    // -- Guest with DeletedAt = None --
    let testGuest2 = { testGuest with DeletedAt = None }
    let guestJson2 = encode testGuest2 |> Encode.toString 0
    match Decode.fromString (decode<Guest>()) guestJson2 with
    | Error err ->
        printfn "FAIL: Guest (None) decode error: %s" err
        pass <- false
    | Ok decoded ->
        check "Guest.DeletedAt None round-trips" (decoded.DeletedAt = None)
        printfn "  Guest (DeletedAt=None) round-trip: OK"

    // -- MicroblogItem --
    let testItem : MicroblogItem = {
        Id = PrimaryKey "item-1"
        Host = MultiTenant "example.com"
        Title = "Hello World"
        Link = Some (Link "https://example.com")
        Image = None
        Extract = Some (RichContent "An extract")
        OwnerComment = RichContent "My comment"
        CreatedAt = CreateTimestamp 1700000000
        UpdatedAt = Some (UpdateTimestamp 1700002000)
        ViewCount = 42
        DeletedAt = None
    }

    let itemJson = encode testItem |> Encode.toString 0
    printfn "  MicroblogItem JSON: %s" itemJson

    match Decode.fromString (decode<MicroblogItem>()) itemJson with
    | Error err ->
        printfn "FAIL: MicroblogItem decode error: %s" err
        pass <- false
    | Ok decoded ->
        let (PrimaryKey id) = decoded.Id
        check "MicroblogItem.Id round-trips" (id = "item-1")
        check "MicroblogItem.Title round-trips" (decoded.Title = "Hello World")
        check "MicroblogItem.Link round-trips" (
            match decoded.Link with
            | Some (Link v) -> v = "https://example.com"
            | None -> false)
        check "MicroblogItem.Image round-trips" (decoded.Image = None)
        check "MicroblogItem.ViewCount round-trips" (decoded.ViewCount = 42)
        check "MicroblogItem.UpdatedAt round-trips" (
            match decoded.UpdatedAt with
            | Some (UpdateTimestamp v) -> v = 1700002000
            | None -> false)
        check "MicroblogItem.DeletedAt round-trips" (decoded.DeletedAt = None)
        printfn "  MicroblogItem round-trip: OK"

    // -- FeedItem (API view type with RichContent) --
    let testFeed : GetFeed.FeedItem = {
        Id = "feed-1"
        Title = "Feed Title"
        Image = Some "https://img.example.com/1.png"
        Extract = Some (RichContent "extract text")
        OwnerComment = RichContent "owner says hi"
        Timestamp = 1700000000
    }

    let feedJson = encode testFeed |> Encode.toString 0
    match Decode.fromString (decode<GetFeed.FeedItem>()) feedJson with
    | Error err ->
        printfn "FAIL: FeedItem decode error: %s" err
        pass <- false
    | Ok decoded ->
        check "FeedItem.Id round-trips" (decoded.Id = "feed-1")
        check "FeedItem.Image round-trips" (decoded.Image = Some "https://img.example.com/1.png")
        check "FeedItem.Extract round-trips" (
            match decoded.Extract with
            | Some (RichContent v) -> v = "extract text"
            | None -> false)
        printfn "  FeedItem round-trip: OK"

    // -- SubmitItem.Request (has string list) --
    let testReq : SubmitItem.Request = {
        Title = "New Post"
        Link = Some "https://example.com/post"
        Image = None
        Extract = Some "A preview"
        OwnerComment = "Check this out"
        Tags = ["fsharp"; "fable"; "web"]
    }

    let reqJson = encode testReq |> Encode.toString 0
    match Decode.fromString (decode<SubmitItem.Request>()) reqJson with
    | Error err ->
        printfn "FAIL: SubmitItem.Request decode error: %s" err
        pass <- false
    | Ok decoded ->
        check "Request.Title round-trips" (decoded.Title = "New Post")
        check "Request.Link round-trips" (decoded.Link = Some "https://example.com/post")
        check "Request.Image round-trips" (decoded.Image = None)
        check "Request.Tags round-trips" (decoded.Tags = ["fsharp"; "fable"; "web"])
        printfn "  SubmitItem.Request round-trip: OK"

    // -- Nested record: SubmitItem.MicroblogItem (has CommentItem list) --
    let testView : SubmitItem.MicroblogItem = {
        Id = "view-1"
        Title = "View Title"
        Link = Some (Link "https://example.com")
        Image = None
        Extract = None
        OwnerComment = RichContent "owner comment"
        Tags = ["tag1"]
        Comments = [
            { Id = "c-1"; ItemId = "view-1"; GuestId = "g-1"
              ParentId = None; AuthorName = "Bob"
              Text = RichContent "Nice post!"; Timestamp = 1700003000 }
        ]
        Timestamp = 1700000000
    }

    let viewJson = encode testView |> Encode.toString 0
    match Decode.fromString (decode<SubmitItem.MicroblogItem>()) viewJson with
    | Error err ->
        printfn "FAIL: MicroblogItemView decode error: %s" err
        pass <- false
    | Ok decoded ->
        check "View.Id round-trips" (decoded.Id = "view-1")
        check "View.Tags round-trips" (decoded.Tags = ["tag1"])
        check "View.Comments length" (List.length decoded.Comments = 1)
        let c = decoded.Comments.[0]
        check "View.Comment.Id round-trips" (c.Id = "c-1")
        check "View.Comment.ParentId round-trips" (c.ParentId = None)
        check "View.Comment.Text round-trips" (
            match c.Text with RichContent v -> v = "Nice post!")
        printfn "  SubmitItem.MicroblogItem (nested) round-trip: OK"

    // ============================================================
    // 6. Validation tests
    // ============================================================
    printfn ""
    printfn "=== Validation ==="

    // Test 1: Valid comment → Ok, verify strings are trimmed
    let validComment : SubmitComment.Request = {
        ItemId = "  item-1  "
        ParentId = Some "  parent-1  "
        Text = "  Hello world  "
        AuthorName = Some "  Alice  "
    }
    match Codecs.Validate.submitCommentReq validComment with
    | Error errs ->
        printfn "FAIL: Valid comment got errors: %A" errs
        pass <- false
    | Ok r ->
        check "Comment.ItemId trimmed" (r.ItemId = "item-1")
        check "Comment.ParentId trimmed" (r.ParentId = Some "parent-1")
        check "Comment.Text trimmed" (r.Text = "Hello world")
        check "Comment.AuthorName trimmed" (r.AuthorName = Some "Alice")
        printfn "  Valid comment (trimmed): OK"

    // Test 2: Empty required fields → Error with correct field names
    let emptyComment : SubmitComment.Request = {
        ItemId = "  "
        ParentId = None
        Text = "  "
        AuthorName = None
    }
    match Codecs.Validate.submitCommentReq emptyComment with
    | Ok _ ->
        printfn "FAIL: Empty comment should fail"
        pass <- false
    | Error errs ->
        check "Empty ItemId error" (errs |> List.exists (fun e -> e.Field = "ItemId"))
        check "Empty Text error" (errs |> List.exists (fun e -> e.Field = "Text"))
        printfn "  Empty required fields: OK (%d errors)" (List.length errs)

    // Test 3: MaxLength violation → Error with "at most" message
    let longComment : SubmitComment.Request = {
        ItemId = "item-1"
        ParentId = None
        Text = String.replicate 10001 "x"
        AuthorName = None
    }
    match Codecs.Validate.submitCommentReq longComment with
    | Ok _ ->
        printfn "FAIL: Long text should fail"
        pass <- false
    | Error errs ->
        check "MaxLength error on Text" (
            errs |> List.exists (fun e -> e.Field = "Text" && e.Message.Contains("at most")))
        printfn "  MaxLength violation: OK"

    // Test 4: Valid item → Ok, verify trim on all string fields
    let validItem : SubmitItem.Request = {
        Title = "  My Title  "
        Link = Some "  https://example.com  "
        Image = Some "  https://img.example.com/1.png  "
        Extract = Some "  An extract  "
        OwnerComment = "  Check this out  "
        Tags = ["fsharp"; "fable"]
    }
    match Codecs.Validate.submitItemReq validItem with
    | Error errs ->
        printfn "FAIL: Valid item got errors: %A" errs
        pass <- false
    | Ok r ->
        check "Item.Title trimmed" (r.Title = "My Title")
        check "Item.Link trimmed" (r.Link = Some "https://example.com")
        check "Item.Image trimmed" (r.Image = Some "https://img.example.com/1.png")
        check "Item.Extract trimmed" (r.Extract = Some "An extract")
        check "Item.OwnerComment trimmed" (r.OwnerComment = "Check this out")
        check "Item.Tags unchanged" (r.Tags = ["fsharp"; "fable"])
        printfn "  Valid item (trimmed): OK"

    // Test 5: Too many tags → Error on Tags field
    let manyTags : SubmitItem.Request = {
        Title = "Title"
        Link = None
        Image = None
        Extract = None
        OwnerComment = "Comment"
        Tags = List.init 21 (fun i -> sprintf "tag-%d" i)
    }
    match Codecs.Validate.submitItemReq manyTags with
    | Ok _ ->
        printfn "FAIL: Too many tags should fail"
        pass <- false
    | Error errs ->
        check "MaxLength error on Tags" (
            errs |> List.exists (fun e -> e.Field = "Tags" && e.Message.Contains("at most")))
        printfn "  Too many tags: OK"

    // Test 6: MinLength violation → Error with "at least" message
    let minLenSchema =
        Hedge.Schema.schema "Test" [
            fieldWith "Name" FString [MinLength 3]
        ]
    let shortVal : {| Name: string |} = {| Name = "ab" |}
    match validate minLenSchema shortVal with
    | Ok _ ->
        printfn "FAIL: MinLength violation should fail"
        pass <- false
    | Error errs ->
        check "MinLength error" (
            errs |> List.exists (fun e -> e.Field = "Name" && e.Message.Contains("at least")))
        printfn "  MinLength violation: OK"

    printfn ""
    printfn "VERDICT: %s" (if pass then "ALL PASS" else "SOME FAILED")
    if pass then 0 else 1
