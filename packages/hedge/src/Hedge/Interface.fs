module Hedge.Interface

/// Schema wrapper types.
///
/// These are single-case DUs that carry semantic meaning through
/// the type system. Unlike attributes, Fable preserves these in
/// its TypeInfo metadata — so we get single source of truth AND
/// runtime reflection.
///
/// Port of: hamlet/packages/buildamp/Interface/Schema.elm
/// (Hamlet uses transparent type aliases; F# uses single-case DUs)

// -- Schema field types --

/// Primary key. Wraps the ID type (usually string).
type PrimaryKey<'a> = PrimaryKey of 'a

/// Auto-populated creation timestamp (epoch seconds).
type CreateTimestamp = CreateTimestamp of int

/// Auto-populated update timestamp (epoch seconds).
/// Field should be `option` — None until first update.
type UpdateTimestamp = UpdateTimestamp of int

/// Soft delete timestamp (epoch seconds).
/// Field should be `option` — None if not deleted.
type SoftDelete = SoftDelete of int

/// A user-editable calendar date (epoch seconds), distinct from the auto
/// CreateTimestamp/UpdateTimestamp: it's the record's own date (article date,
/// release date) that drives display/sort/grouping and the author sets. The admin
/// renders it as a date picker — by this type, not a field-name convention.
type EditableDate = EditableDate of int

/// Foreign key reference. Phantom type 'table carries the
/// referenced model for documentation; value is the ID string.
type ForeignKey<'table> = ForeignKey of string

/// Reference to the shared, app-level identity (the guest/identity layer).
/// A typed handle that does NOT depend on any module's concrete Identity type,
/// so a module can reference an identity without coupling to the host's Models.
/// Gen treats it as a TEXT column with a FK to the shared `identities` table.
type IdentityRef = IdentityRef of string

/// Rich content — a TipTap/ProseMirror document stored as a JSON string. The admin
/// editor produces it and the rich-text viewer pipeline renders it (some legacy rows,
/// e.g. basewatch, hold archived HTML rendered as-is).
type RichContent = RichContent of string

/// URL / link.
type Link = Link of string

/// Unique constraint. Wraps the value type.
type Unique<'a> = Unique of 'a

// -- Attributes --

/// Table name override for code generation.
[<AllowNullLiteral>]
type TableAttribute(name: string) =
    inherit System.Attribute()
    member _.Name = name

// -- API endpoint types --
// The GET family is a 2x2 over (path parameter? x typed query?). A query type is a
// record whose fields Gen turns into ?k=v params (string/int, each optional or
// required); the client serializes them and the server parses them, so a handler
// receives a typed query record rather than picking a raw query string apart.

/// GET endpoint, static path, no query. Phantom type carries the response shape.
type Get<'resp> = Get of string

/// GET endpoint, static path + typed query parameters.
type GetQuery<'query, 'resp> = GetQuery of string

/// GET endpoint with one path parameter. Phantom type carries the response shape.
/// (Formerly `GetOne`.)
type GetBy<'resp> = GetBy of (string -> string)

/// GET endpoint with one path parameter + typed query parameters.
type GetByQuery<'query, 'resp> = GetByQuery of (string -> string)

/// POST endpoint. Phantom types carry request and response shapes.
type Post<'req, 'resp> = Post of string
