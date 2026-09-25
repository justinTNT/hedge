module Models.Domain
open Hedge.Interface

/// Source accounts and owner edits. Private contributions have their own records below.
[<Table "plants">]
[<AdminList "WHERE deleted_at IS NULL ORDER BY scientific_name LIMIT 1000">]
type Plant = {
    Id: PrimaryKey<string>
    Slug: Unique<string>
    ScientificName: string
    CommonNames: string
    Family: string
    Genus: string
    Aliases: string
    Forms: string
    Height: string
    Sun: string
    Water: string
    GardenFeatures: string
    Wildlife: string
    Habit: string
    Bark: string
    Leaves: string
    Phyllodes: string
    Flowers: string
    Fruit: string
    Flowering: string
    Fruiting: string
    Features: string
    Habitat: string
    Cultivation: string
    TraditionalUses: string
    Notes: string
    Distribution: string
    SourceReferences: string
    SourceEvidence: string
    EndemicNt: bool
    Published: bool
    SortOrder: int
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

[<Table "plant_photos">]
[<AdminList "WHERE deleted_at IS NULL ORDER BY plant_id, sort_order LIMIT 2000">]
type PlantPhoto = {
    Id: PrimaryKey<string>
    PlantId: ForeignKey<Plant>
    Image: Image
    Thumbnail: Image
    Caption: string
    Photographer: string
    SortOrder: int
    Published: bool
    SourceEvidence: string
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

/// Authored distribution illustrations; separate from photographs and their hero order.
[<Table "plant_maps">]
[<AdminList "WHERE deleted_at IS NULL ORDER BY plant_id, sort_order LIMIT 1000">]
type PlantMap = {
    Id: PrimaryKey<string>
    PlantId: ForeignKey<Plant>
    Image: Image
    Caption: string
    SourceLabel: string
    Published: bool
    SortOrder: int
    SourceEvidence: string
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

/// App-owned botanical vocabulary; published entries annotate account prose.
[<Table "glossary_terms">]
[<AdminList "WHERE deleted_at IS NULL ORDER BY term LIMIT 1000">]
type GlossaryTerm = {
    Id: PrimaryKey<string>
    Term: Unique<string>
    Aliases: string
    Definition: string
    Illustration: Image
    SourceLabel: string
    SourceEvidence: string
    SortOrder: int
    Published: bool
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

/// Numbered usage citations and bibliography paragraphs retain separate source identities.
[<Table "source_references">]
[<AdminList "WHERE deleted_at IS NULL ORDER BY kind, sort_order LIMIT 1000">]
type SourceReference = {
    Id: PrimaryKey<string>
    SourceKey: Unique<string>
    Kind: string
    Number: int
    Citation: string
    Aliases: string
    SourceLabel: string
    SourceEvidence: string
    SortOrder: int
    Published: bool
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}


/// Private app content. Never exposed through generic admin CRUD or the public snapshot.
[<Table "plant_notes">]
type PlantNote = {
    Id: PrimaryKey<string>
    PlantId: ForeignKey<Plant>
    OwnerProvider: string
    OwnerId: string
    Text: string
    IsCorrection: bool
    Revision: int
    ReviewedRevision: int option
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

[<Table "personal_plant_photos">]
type PersonalPlantPhoto = {
    Id: PrimaryKey<string>
    PlantId: ForeignKey<Plant>
    OwnerProvider: string
    OwnerId: string
    ImageKey: string
    ThumbnailKey: string
    Width: int
    Height: int
    StoredBytes: int
    Caption: string
    Photographer: string
    Offered: bool
    Ready: bool
    Revision: int
    PublishedPhotoId: string option
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

[<Table "plant_view_preferences">]
[<UniqueTogether("OwnerProvider", "OwnerId", "PlantId")>]
type PlantViewPreference = {
    Id: PrimaryKey<string>
    OwnerProvider: string
    OwnerId: string
    PlantId: ForeignKey<Plant>
    HeroPhotoId: string option
}

/// Tombstone: a claimed anonymous session cannot create orphaned content after OAuth adoption.
[<Table "contribution_claims">]
type ContributionClaim = {
    Id: PrimaryKey<string>
    GuestId: Unique<string>
    CreatedAt: CreateTimestamp
}
