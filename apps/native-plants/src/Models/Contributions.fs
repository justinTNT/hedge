module Models.Contributions

/// Private wire data: arrays keep this hand-written, multipart-capable surface ordinary JSON.
type Note = { Id:string; Text:string; Correction:bool; Revision:int; Read:bool; CreatedAt:int }
type Photo = {
    Id:string; Image:string; Thumbnail:string; Caption:string; Photographer:string
    Width:int; Height:int; Offered:bool; Revision:int; PublicPhotoId:string
}
/// Server policy counts; photo usage includes in-flight reservations, not just visible photos.
type Capacity = { Used:int; Limit:int }
type Personal = {
    Anonymous:bool; ViewerToken:string; Notes:Note array; Photos:Photo array; HeroPhotoId:string
    NoteCapacity:Capacity; PhotoCapacity:Capacity
}
/// Current request permissions, computed by the same policy as the protected endpoints.
type Capabilities = { CanEditCatalogue:bool; CanReview:bool }
type ReviewNote = { PlantId:string; PlantName:string; Note:Note }
type ReviewPhoto = { PlantId:string; PlantName:string; Photo:Photo }
type Review = { Notes:ReviewNote array; Photos:ReviewPhoto array; Page:int; HasMore:bool }
