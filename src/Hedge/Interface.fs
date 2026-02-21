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

/// Multi-tenant host identifier.
type MultiTenant = MultiTenant of string

/// Auto-populated creation timestamp (epoch seconds).
type CreateTimestamp = CreateTimestamp of int

/// Auto-populated update timestamp (epoch seconds).
/// Field should be `option` — None until first update.
type UpdateTimestamp = UpdateTimestamp of int

/// Soft delete timestamp (epoch seconds).
/// Field should be `option` — None if not deleted.
type SoftDelete = SoftDelete of int

/// Foreign key reference. Phantom type 'table carries the
/// referenced model for documentation; value is the ID string.
type ForeignKey<'table> = ForeignKey of string

/// Rich content (markdown, HTML, structured JSON).
type RichContent = RichContent of string

/// URL / link.
type Link = Link of string
