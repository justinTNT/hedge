module Models.Domain
open Hedge.Interface

/// Source accounts and owner edits; identity and contributions arrive through the shared module later.
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
