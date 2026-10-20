module Models.Domain

open Hedge.Interface

type Post = {
    Id: PrimaryKey<string>
    Title: string
    Body: string
    CreatedAt: CreateTimestamp
}
