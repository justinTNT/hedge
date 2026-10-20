module Models.Api

open Hedge.Interface

module GetPosts =
    type PostItem = {
        Id: string
        Title: string
        Body: string
        Timestamp: int
    }

    type Response = {
        Posts: PostItem list
    }

    let endpoint : Get<Response> = Get "/api/posts"

module CreatePost =
    type Request = {
        Title: string
        Body: string
    }

    type Response = {
        Post: GetPosts.PostItem
    }

    let endpoint : Post<Request, Response> = Post "/api/post"
