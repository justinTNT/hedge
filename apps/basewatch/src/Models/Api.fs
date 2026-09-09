module Models.Api

open Hedge.Interface

module GetSite =
    /// One flat menu node — the client assembles the tree from Parent + Ordinal.
    type MenuNode = {
        Item: string
        Title: string
        Link: string
        Parent: string
        Ordinal: int
    }
    /// The whole nav in one shot (it's tiny), plus which page is home.
    type Response = {
        Menu: MenuNode list
        Home: string
    }
    let endpoint : Get<Response> = Get "/api/site"

module GetPage =
    /// A page's content, fetched on navigation.
    type PageView = {
        Name: string
        Title: string
        Teaser: string
        Body: string
    }
    type Response = { Page: PageView }
    // Path param is the page Name (the URL slug).
    let endpoint : GetOne<Response> = GetOne (sprintf "/api/page/%s")
