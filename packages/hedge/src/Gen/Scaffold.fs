module Gen.Scaffold

open System
open System.IO

// ============================================================
// Helpers
// ============================================================

let ensureDir (path: string) =
    let dir = Path.GetDirectoryName(path)
    if dir <> "" && dir <> null && not (Directory.Exists dir) then
        Directory.CreateDirectory(dir) |> ignore

let writeFile (root: string) (relPath: string) (content: string) =
    let fullPath = Path.Combine(root, relPath)
    ensureDir fullPath
    File.WriteAllText(fullPath, content)
    printfn "  %s" relPath

/// Triple-quote placeholder — templates use TQTQ which gets replaced with """ at emit time
let private tq = "\"\"\""
let private fixTQ (s: string) = s.Replace("TQTQ", tq)

let toPascalCase (s: string) =
    s.Split([|'-'; '_'|], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun part -> string (Char.ToUpper part.[0]) + part.[1..])
    |> String.concat ""

// ============================================================
// Static files (100% generic, no parameterization)
// ============================================================

let nvmrc = "stable\n"

let viteConfigJs = """import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    port: 3030,
    host: true,
    allowedHosts: true,
    proxy: {
      '/api': {
        target: 'http://localhost:8787',
        changeOrigin: true,
        ws: true
      },
      '/blobs': {
        target: 'http://localhost:8787',
        changeOrigin: true
      }
    }
  }
});
"""

let workerEntryJs = """export { default } from "./dist/server/Worker.js";
export { EventHub } from "./dist/server/EventHub.js";
"""

let guestSessionJs = """(function () {
  var KEY = 'hedge_guest_session';
  var adjectives = ['Sleepy','Brave','Grumpy','Neon','Ancient','Quantum','Wandering','Clever',
    'Daring','Gentle','Happy','Keen','Lively','Merry','Noble','Proud',
    'Quick','Sharp','Swift','Tall','Warm','Wild','Wise','Bold',
    'Bright','Cool','Fair','Calm','Fierce','Eager','Sunny','Lucky',
    'Jolly','Silly','Chilly','Cosmic','Mystic','Lunar','Solar','Stellar',
    'Astral','Galactic','Epic','Heroic','Magic','Secret','Hidden','Lost',
    'Found','Quiet','Loud','Fuzzy','Spiky','Smooth','Rough','Soft',
    'Hard','Sweet','Sour','Spicy','Salty','Bitter','Fresh','Stale',
    'Crisp','Crunchy','Chewy','Sticky','Slippery','Shiny','Dull','Dark',
    'Light','Heavy','Empty','Full','Hollow','Solid','Liquid','Gas',
    'Hot','Cold','Freezing','Boiling','Fast','Slow','Sluggish','Rapid',
    'Leisurely','Hasty','Deliberate','Young','Old','New','Modern','Classic',
    'Vintage','Retro','Tiny','Small','Medium','Large','Huge','Giant',
    'Massive','Colossal','Good','Bad','Great','Terrible','Excellent','Awful',
    'Wonderful','Horrible','Sad','Joyful','Sorrowful','Glad','Upset','Cheerful',
    'Miserable','Angry','Furious','Peaceful','Mad','Tranquil','Enraged','Serene',
    'Cowardly','Courageous','Fearful','Fearless','Timid','Afraid','Smart','Stupid',
    'Foolish','Intelligent','Ignorant','Unwise','Rich','Poor','Wealthy','Impoverished',
    'Affluent','Destitute','Prosperous','Needy','Beautiful','Ugly','Gorgeous','Hideous',
    'Attractive','Unattractive','Handsome','Plain','Clean','Dirty','Spotless','Filthy',
    'Immaculate','Grubby','Pristine','Messy','Dry','Wet','Arid','Damp',
    'Parched','Moist','Dehydrated','Soaked'];
  var colors = [
    {name:'Slate',hex:'#64748b'},{name:'Gray',hex:'#6b7280'},{name:'Zinc',hex:'#71717a'},
    {name:'Neutral',hex:'#737373'},{name:'Stone',hex:'#78716c'},{name:'Red',hex:'#ef4444'},
    {name:'Orange',hex:'#f97316'},{name:'Amber',hex:'#f59e0b'},{name:'Yellow',hex:'#eab308'},
    {name:'Lime',hex:'#84cc16'},{name:'Green',hex:'#22c55e'},{name:'Emerald',hex:'#10b981'},
    {name:'Teal',hex:'#14b8a6'},{name:'Cyan',hex:'#06b6d4'},{name:'Sky',hex:'#0ea5e9'},
    {name:'Blue',hex:'#3b82f6'},{name:'Indigo',hex:'#6366f1'},{name:'Violet',hex:'#8b5cf6'},
    {name:'Purple',hex:'#a855f7'},{name:'Fuchsia',hex:'#d946ef'},{name:'Pink',hex:'#ec4899'},
    {name:'Rose',hex:'#f43f5e'},{name:'Coral',hex:'#ff7f50'},{name:'Salmon',hex:'#fa8072'},
    {name:'Tomato',hex:'#ff6347'},{name:'Gold',hex:'#ffd700'},{name:'Olive',hex:'#808000'},
    {name:'Navy',hex:'#000080'},{name:'Maroon',hex:'#800000'},{name:'Plum',hex:'#dda0dd'}];
  var emojis = [
    {c:'🐒',n:'Monkey'},{c:'🦍',n:'Gorilla'},{c:'🐕',n:'Dog'},{c:'🐩',n:'Poodle'},
    {c:'🐺',n:'Wolf'},{c:'🦊',n:'Fox'},{c:'🐈',n:'Cat'},{c:'🦁',n:'Lion'},
    {c:'🐅',n:'Tiger'},{c:'🐆',n:'Leopard'},{c:'🐎',n:'Horse'},{c:'🦄',n:'Unicorn'},
    {c:'🦓',n:'Zebra'},{c:'🦌',n:'Deer'},{c:'🐄',n:'Cow'},{c:'🐂',n:'Ox'},
    {c:'🐃',n:'Buffalo'},{c:'🐖',n:'Pig'},{c:'🐗',n:'Boar'},{c:'🐏',n:'Ram'},
    {c:'🐑',n:'Sheep'},{c:'🐐',n:'Goat'},{c:'🐪',n:'Camel'},{c:'🦙',n:'Llama'},
    {c:'🦒',n:'Giraffe'},{c:'🐘',n:'Elephant'},{c:'🦏',n:'Rhino'},{c:'🦛',n:'Hippo'},
    {c:'🐁',n:'Mouse'},{c:'🐀',n:'Rat'},{c:'🐹',n:'Hamster'},{c:'🐇',n:'Rabbit'},
    {c:'🐿️',n:'Chipmunk'},{c:'🦔',n:'Hedgehog'},{c:'🦇',n:'Bat'},{c:'🐻',n:'Bear'},
    {c:'🐨',n:'Koala'},{c:'🐼',n:'Panda'},{c:'🦥',n:'Sloth'},{c:'🦦',n:'Otter'},
    {c:'🦨',n:'Skunk'},{c:'🦘',n:'Kangaroo'},{c:'🦡',n:'Badger'},{c:'🦃',n:'Turkey'},
    {c:'🐔',n:'Hen'},{c:'🐓',n:'Rooster'},{c:'🐦',n:'Bird'},{c:'🐧',n:'Penguin'},
    {c:'🕊️',n:'Dove'},{c:'🦅',n:'Eagle'},{c:'🦆',n:'Duck'},{c:'🦢',n:'Swan'},
    {c:'🦉',n:'Owl'},{c:'🦩',n:'Flamingo'},{c:'🦚',n:'Peacock'},{c:'🦜',n:'Parrot'},
    {c:'🐸',n:'Frog'},{c:'🐊',n:'Croc'},{c:'🐢',n:'Turtle'},{c:'🦎',n:'Lizard'},
    {c:'🐍',n:'Snake'},{c:'🐉',n:'Dragon'},{c:'🦕',n:'Dino'},{c:'🦖',n:'Rex'},
    {c:'🐋',n:'Whale'},{c:'🐬',n:'Dolphin'},{c:'🦭',n:'Seal'},{c:'🐟',n:'Fish'},
    {c:'🐡',n:'Puffer'},{c:'🦈',n:'Shark'},{c:'🐙',n:'Octopus'},{c:'🐌',n:'Snail'},
    {c:'🦋',n:'Butterfly'},{c:'🐛',n:'Bug'},{c:'🐜',n:'Ant'},{c:'🐝',n:'Bee'},
    {c:'🪲',n:'Beetle'},{c:'🐞',n:'Ladybug'},{c:'🦗',n:'Cricket'},{c:'🕷️',n:'Spider'},
    {c:'🦂',n:'Scorpion'},{c:'🦟',n:'Mosquito'},{c:'🪰',n:'Fly'},{c:'🪱',n:'Worm'},
    {c:'🦠',n:'Microbe'},{c:'💐',n:'Bouquet'},{c:'🌸',n:'Blossom'},{c:'💮',n:'Flower'},
    {c:'🏵️',n:'Rosette'},{c:'🌹',n:'Rose'},{c:'🥀',n:'Wilt'},{c:'🌺',n:'Hibiscus'},
    {c:'🌻',n:'Sunflower'},{c:'🌼',n:'Daisy'},{c:'🌷',n:'Tulip'},{c:'🌱',n:'Seedling'},
    {c:'🪴',n:'Plant'},{c:'🌲',n:'Pine'},{c:'🌳',n:'Oak'},{c:'🌴',n:'Palm'},
    {c:'🌵',n:'Cactus'},{c:'🌾',n:'Grain'},{c:'🌿',n:'Fern'},{c:'☘️',n:'Clover'},
    {c:'🍀',n:'Shamrock'},{c:'🍁',n:'Maple'},{c:'🍂',n:'Leaf'},{c:'🍃',n:'Breeze'},
    {c:'🍄',n:'Mushroom'},{c:'🌰',n:'Chestnut'},{c:'🦀',n:'Crab'},{c:'🦞',n:'Lobster'},
    {c:'🦐',n:'Shrimp'},{c:'🦑',n:'Squid'},{c:'🌍',n:'Globe'},{c:'🌙',n:'Moon'},
    {c:'☀️',n:'Sun'},{c:'⭐',n:'Star'},{c:'⚡',n:'Bolt'},{c:'🌊',n:'Wave'},
    {c:'🔥',n:'Fire'},{c:'💧',n:'Drop'},{c:'❄️',n:'Snow'},{c:'🌬️',n:'Gust'},
    {c:'🎸',n:'Guitar'},{c:'🎺',n:'Trumpet'},{c:'🎻',n:'Violin'},{c:'🥁',n:'Drum'},
    {c:'🚀',n:'Rocket'},{c:'🚁',n:'Copter'},{c:'⛵',n:'Boat'},{c:'⚓',n:'Anchor'},
    {c:'⛺',n:'Tent'},{c:'🧭',n:'Compass'},{c:'🗺️',n:'Atlas'},{c:'🔮',n:'Crystal'},
    {c:'🪄',n:'Wand'},{c:'💎',n:'Gem'},{c:'🧲',n:'Magnet'},{c:'🔭',n:'Scope'},
    {c:'🔬',n:'Lens'},{c:'🛰️',n:'Satellite'},{c:'💡',n:'Bulb'},{c:'🔦',n:'Torch'},
    {c:'🏮',n:'Lantern'},{c:'📚',n:'Books'},{c:'📜',n:'Scroll'},{c:'🔑',n:'Key'},
    {c:'🎈',n:'Balloon'},{c:'🪁',n:'Kite'},{c:'🧸',n:'Teddy'},{c:'🧩',n:'Puzzle'},
    {c:'🚲',n:'Bike'},{c:'🛹',n:'Board'},{c:'🛼',n:'Skate'},{c:'🎫',n:'Ticket'},
    {c:'🏆',n:'Trophy'},{c:'🥇',n:'Medal'},{c:'👑',n:'Crown'},{c:'👻',n:'Ghost'},
    {c:'👽',n:'Alien'},{c:'👾',n:'Invader'},{c:'🤖',n:'Robot'},{c:'🦴',n:'Bone'},
    {c:'🦷',n:'Tooth'},{c:'👁️',n:'Eye'},{c:'🧠',n:'Brain'},{c:'❤️',n:'Heart'},
    {c:'🍎',n:'Apple'},{c:'🍐',n:'Pear'},{c:'🍊',n:'Orange'},{c:'🍋',n:'Lemon'},
    {c:'🍌',n:'Banana'},{c:'🍉',n:'Melon'},{c:'🍇',n:'Grape'},{c:'🍓',n:'Berry'},
    {c:'🫐',n:'Blueberry'},{c:'🍈',n:'Honeydew'},{c:'🍒',n:'Cherry'},{c:'🍑',n:'Peach'},
    {c:'🥭',n:'Mango'},{c:'🍍',n:'Pineapple'},{c:'🥥',n:'Coconut'},{c:'🥝',n:'Kiwi'},
    {c:'🍅',n:'Tomato'},{c:'🍆',n:'Eggplant'},{c:'🥑',n:'Avocado'},{c:'🥦',n:'Broccoli'},
    {c:'🥬',n:'Chard'},{c:'🥒',n:'Cucumber'},{c:'🫑',n:'Pepper'},{c:'🌶️',n:'Chili'},
    {c:'🌽',n:'Corn'},{c:'🥕',n:'Carrot'},{c:'🧄',n:'Garlic'},{c:'🧅',n:'Onion'},
    {c:'🥔',n:'Potato'},{c:'🍠',n:'Yam'},{c:'🥐',n:'Croissant'},{c:'🥯',n:'Bagel'},
    {c:'🍞',n:'Bread'},{c:'🥖',n:'Baguette'},{c:'🥨',n:'Pretzel'},{c:'🧀',n:'Cheese'},
    {c:'🥚',n:'Egg'},{c:'🍳',n:'Skillet'},{c:'🧈',n:'Butter'},{c:'🥞',n:'Pancake'},
    {c:'🧇',n:'Waffle'},{c:'🥓',n:'Bacon'},{c:'🥩',n:'Steak'},{c:'🍗',n:'Drumstick'},
    {c:'🍖',n:'Rib'},{c:'🌭',n:'Hotdog'},{c:'🍔',n:'Burger'},{c:'🍟',n:'Fries'},
    {c:'🍕',n:'Pizza'},{c:'🫓',n:'Flatbread'},{c:'🥪',n:'Sandwich'},{c:'🥙',n:'Pita'},
    {c:'🧆',n:'Falafel'},{c:'🌮',n:'Taco'},{c:'🌯',n:'Burrito'},{c:'🫔',n:'Tamale'},
    {c:'🥗',n:'Salad'},{c:'🥘',n:'Stew'},{c:'🫕',n:'Fondue'},{c:'🥫',n:'Can'}];
  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }
  function hash(s) { var h = 0; for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return Math.abs(h); }
  function pickH(a, h) { return a[h % a.length]; }
  function makeAvatar(hex, emoji) {
    return 'data:image/svg+xml,' + encodeURIComponent(
      '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64">' +
      '<circle cx="32" cy="32" r="32" fill="' + hex + '"/>' +
      '<text x="32" y="32" text-anchor="middle" dominant-baseline="central" font-size="36">' +
      emoji + '</text></svg>');
  }
  function getSession() {
    var s = localStorage.getItem(KEY);
    if (s) {
      try {
        var p = JSON.parse(s);
        if (p && !p.avatarUrl) {
          var h = hash(p.guestId);
          var c = pickH(colors, h);
          var e = pickH(emojis, h >>> 5);
          p.avatarHex = c.hex;
          p.avatarChar = e.c;
          p.avatarUrl = makeAvatar(c.hex, e.c);
          p.displayName = pickH(adjectives, h >>> 10) + ' ' + c.name + ' ' + e.n;
          localStorage.setItem(KEY, JSON.stringify(p));
        }
        return p;
      } catch(_) {}
    }
    var c = pick(colors);
    var e = pick(emojis);
    var n = { guestId: 'guest-' + Math.random().toString(36).substring(2,10),
              displayName: pick(adjectives) + ' ' + c.name + ' ' + e.n,
              avatarHex: c.hex,
              avatarChar: e.c,
              avatarUrl: makeAvatar(c.hex, e.c),
              createdAt: Math.floor(Date.now()/1000) };
    localStorage.setItem(KEY, JSON.stringify(n));
    return n;
  }
  window.HedgeGuest = { getSession: getSession };
})();
"""

let workerFs = """module Server.Worker

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Server.Env

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        Server.Routes.dispatch request (env :?> Env) ctx
    Admin = Some (fun request env route ->
        Server.Admin.handleRequest request (env :?> Env) route)
}
"""

let envFs = """module Server.Env

open Hedge.Workers

type Env = {
    DB: D1Database
    EVENTS: DurableObjectNamespace
    BLOBS: R2Bucket
    ADMIN_KEY: string
    ENVIRONMENT: string
}
"""

let adminFs = """module Server.Admin

open Fable.Core
open Thoth.Json
open Hedge.Workers
open Hedge.Router
open Hedge.SchemaCodec
open Server.Env
open Server.AdminConfig

let checkAdmin (request: WorkerRequest) (env: Env) : bool =
    let key = getHeader request "X-Admin-Key"
    key <> "" && key = env.ADMIN_KEY

let private findEntity (name: string) =
    entities |> List.tryFind (fun e -> e.Name = name)

let private typesResponse () : WorkerResponse =
    let body =
        Encode.object [
            "types", Encode.list (entities |> List.map (fun e ->
                Encode.object [
                    "name", Encode.string e.Name
                    "schema", encodeTypeSchema e.Schema
                ]))
        ] |> Encode.toString 0
    okJson body

let private listResponse (entity: AdminEntity) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! json = entity.List env
        let body = sprintf TQTQ{"records":%s}TQTQ json
        return okJson body
    }

let private getResponse (entity: AdminEntity) (id: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! result = entity.Get id env
        match result with
        | None -> return notFound ()
        | Some json ->
            let body = sprintf TQTQ{"record":%s}TQTQ json
            return okJson body
    }

let private updateResponse (entity: AdminEntity) (id: string) (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! bodyText = request.text()
        let! json = entity.Update id bodyText env
        let body = sprintf TQTQ{"record":%s}TQTQ json
        return okJson body
    }

let private deleteResponse (entity: AdminEntity) (id: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        do! entity.Delete id env
        return okJson TQTQ{"ok":true}TQTQ
    }

/// Try to handle an admin route. Returns Some promise if matched, None otherwise.
let handleRequest (request: WorkerRequest) (env: Env) (route: Route) : JS.Promise<WorkerResponse> option =
    match route with
    // GET /api/admin/types — list available schemas
    | GET path when matchPath "/api/admin/types" path = Some (Exact "/api/admin/types") ->
        Some (promise { return typesResponse () })

    // GET /api/admin/:type — list records
    | GET path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, typeName)) ->
            // Check for /:type/:id pattern (path has extra segment)
            let parts = typeName.Split('/')
            if parts.Length = 1 then
                match findEntity typeName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! listResponse entity env
                    })
                | None -> None
            elif parts.Length = 2 then
                let entityName = parts.[0]
                let recordId = parts.[1]
                match findEntity entityName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! getResponse entity recordId env
                    })
                | None -> None
            else None
        | _ -> None

    // PUT /api/admin/:type/:id — update record
    | PUT path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, rest)) ->
            let parts = rest.Split('/')
            if parts.Length = 2 then
                let entityName = parts.[0]
                let recordId = parts.[1]
                match findEntity entityName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! updateResponse entity recordId request env
                    })
                | None -> None
            else None
        | _ -> None

    // DELETE /api/admin/:type/:id — delete record
    | DELETE path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, rest)) ->
            let parts = rest.Split('/')
            if parts.Length = 2 then
                let entityName = parts.[0]
                let recordId = parts.[1]
                match findEntity entityName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! deleteResponse entity recordId env
                    })
                | None -> None
            else None
        | _ -> None

    | _ -> None
"""

let adminConfigFs = """module Server.AdminConfig

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers
open Hedge.Schema
open Server.Env
open Server.AdminGen

/// An admin-manageable entity. Each entity provides a schema
/// and handler functions for CRUD operations.
type AdminEntity = {
    Name: string
    Schema: TypeSchema
    List: Env -> JS.Promise<string>
    Get: string -> Env -> JS.Promise<string option>
    Update: string -> string -> Env -> JS.Promise<string>
    Delete: string -> Env -> JS.Promise<unit>
}

// ============================================================
// PascalCase -> camelCase (for JSON keys)
// ============================================================

let private camelCase (s: string) =
    if s.Length = 0 then s
    else string (System.Char.ToLowerInvariant s.[0]) + s.[1..]

// ============================================================
// PascalCase -> snake_case (for DB column names)
// ============================================================

let private toSnakeCase (s: string) =
    s.ToCharArray()
    |> Array.mapi (fun i c ->
        if i > 0 && System.Char.IsUpper c then
            sprintf "_%c" (System.Char.ToLower c)
        else
            string (System.Char.ToLower c))
    |> String.concat ""

// ============================================================
// Row -> JSON (generic, driven by schema)
// ============================================================

let private rowToJson (schema: TypeSchema) (row: obj) : JsonValue =
    let pairs =
        schema.Fields |> List.map (fun field ->
            let col = toSnakeCase field.Name
            let jsonKey = camelCase field.Name
            let v = getProp row col
            let encoded =
                match field.Type with
                | FInt -> if isNull v then Encode.nil else Encode.int (unbox v)
                | FBool -> if isNull v then Encode.nil else Encode.bool (unbox v)
                | FOption _ -> if isNull v then Encode.nil else Encode.string (unbox v)
                | FList FString -> Encode.list []
                | _ -> if isNull v then Encode.nil else Encode.string (unbox v)
            jsonKey, encoded)
    Encode.object pairs

// ============================================================
// Generic CRUD handlers
// ============================================================

let private genericList (table: AdminTable) (env: Env) : JS.Promise<string> =
    promise {
        let! result = env.DB.prepare(table.SelectAll).all()
        let items =
            result.results
            |> Array.map (rowToJson table.Schema)
            |> Array.toList
        return Encode.list items |> Encode.toString 0
    }

let private genericGet (table: AdminTable) (id: string) (env: Env) : JS.Promise<string option> =
    promise {
        let stmt = bind (env.DB.prepare(table.SelectOne)) [| box id |]
        let! result = stmt.all()
        if result.results.Length = 0 then
            return None
        else
            let json = rowToJson table.Schema result.results.[0]
            return Some (Encode.toString 0 json)
    }

let private genericUpdate (table: AdminTable) (id: string) (body: string) (env: Env) : JS.Promise<string> =
    promise {
        match Decode.fromString (Decode.keyValuePairs Decode.value) body with
        | Error err -> return sprintf TQTQ{"error":"%s"}TQTQ err
        | Ok pairs ->
            let pairMap = pairs |> Map.ofList
            let args =
                table.MutableFields |> List.map (fun fieldName ->
                    let jsonKey = camelCase fieldName
                    match Map.tryFind jsonKey pairMap with
                    | Some v ->
                        let s = Encode.toString 0 v
                        if s = "null" then jsNull
                        else
                            // Strip quotes from string values
                            let trimmed = s.Trim('"')
                            box trimmed
                    | None -> jsNull)
            let allArgs = args @ [box id] |> List.toArray
            let stmt = bind (env.DB.prepare(table.Update)) allArgs
            let! _ = stmt.run()
            let! result = genericGet table id env
            return result |> Option.defaultValue TQTQ{"error":"Not found after update"}TQTQ
    }

let private genericDelete (table: AdminTable) (id: string) (env: Env) : JS.Promise<unit> =
    promise {
        let stmt = bind (env.DB.prepare(table.Delete)) [| box id |]
        let! _ = stmt.run()
        ()
    }

// ============================================================
// Entity registry — built from AdminGen.tables
// ============================================================

let entities : AdminEntity list =
    AdminGen.tables |> List.map (fun table ->
        { Name = table.Name
          Schema = table.Schema
          List = genericList table
          Get = genericGet table
          Update = genericUpdate table
          Delete = genericDelete table })
"""

let eventHubFs = """module Server.EventHub

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Server.Env

[<AttachMembers>]
type EventHub(state: DurableObjectState, _env: Env) =

    member _.fetch(request: WorkerRequest) : JS.Promise<WorkerResponse> =
        promise {
            if isWebSocketUpgrade request then
                let pair = createWebSocketPair ()
                state.acceptWebSocket pair.[1]
                return upgradeResponse pair.[0]
            else
                let! body = request.text()
                for ws in state.getWebSockets() do
                    try ws.send body with _ -> ()
                let options = createObj [ "status" ==> 200 ]
                return WorkerResponse.create(TQTQ{"ok":true}TQTQ, options)
        }

    member _.webSocketMessage(_ws: WebSocket, _msg: string) : unit = ()
    member _.webSocketClose(_ws: WebSocket, _code: int, _reason: string, _wasClean: bool) : unit = ()
"""

let clientApiFs = """module Client.Api

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json

/// Framework HTTP helpers — typed API functions are in generated/ClientGen.fs.

let fetchJson<'T> (url: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url []
        let! text = response.text()
        return Decode.fromString decoder text
    }

let postJson<'T> (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url [
            Method HttpMethod.POST
            requestHeaders [ ContentType "application/json" ]
            Body (BodyInit.Case3 body)
        ]
        let! text = response.text()
        return Decode.fromString decoder text
    }

// -- WebSocket --

[<Emit("(window.location.protocol === 'https:' ? 'wss://' : 'ws://') + window.location.host")>]
let wsBase () : string = jsNative

[<Emit(TQTQ
  (function() {
    var ws = new WebSocket($0);
    ws.onmessage = $1;
    ws.onerror = $2;
    return function() { ws.close(); };
  })()
TQTQ)>]
let openWebSocket (url: string) (onMessage: obj -> unit) (onError: obj -> unit) : (unit -> unit) = jsNative
"""

let guestSessionFs = """module Client.GuestSession

open Fable.Core
open Fable.Core.JsInterop

type GuestSessionData = {
    GuestId: string
    DisplayName: string
    AvatarHex: string
    AvatarChar: string
    AvatarUrl: string
}

[<Emit("window.HedgeGuest.getSession()")>]
let private getRawSession () : obj = jsNative

[<Emit("$0 || ''")>]
let private orEmpty (x: obj) : string = jsNative

let getSession () : GuestSessionData =
    let raw = getRawSession ()
    { GuestId = raw?guestId
      DisplayName = raw?displayName
      AvatarHex = orEmpty raw?avatarHex
      AvatarChar = orEmpty raw?avatarChar
      AvatarUrl = orEmpty raw?avatarUrl }
"""

let richTextFs = """module Client.RichText

open Fable.Core

// Element ID constants
let commentEditorId = "comment-editor"
let ownerCommentEditorId = "owner-comment-editor"

// Editor lifecycle (deferred — waits for DOM element to appear)

[<Emit("window.HedgeRT.waitForElement($0, function() { window.HedgeRT.createRichTextEditor({ elementId: $0, initialContent: $1, onChange: null }); })")>]
let createEditorWhenReady (elementId: string) (initialContent: string) : unit = jsNative

[<Emit("window.HedgeRT.destroyRichTextEditor($0)")>]
let destroyEditor (elementId: string) : unit = jsNative

[<Emit("window.HedgeRT.getEditorContentJSON($0)")>]
let getEditorContent (elementId: string) : string = jsNative

[<Emit("(function(){ var e = window.HedgeRT.getEditor($0); if(e) e.commands.clearContent(); })()")>]
let clearEditor (elementId: string) : unit = jsNative

// Viewer lifecycle (deferred — waits for DOM element to appear)

[<Emit("window.HedgeRT.waitForElement($0, function() { window.HedgeRT.createRichTextViewer({ elementId: $0, content: $1 }); })")>]
let createViewerWhenReady (elementId: string) (content: string) : unit = jsNative

[<Emit("window.HedgeRT.destroyRichTextViewer($0)")>]
let destroyViewer (elementId: string) : unit = jsNative

// Plain text extraction

[<Emit("window.HedgeRT.extractPlainText($0)")>]
let extractPlainText (jsonString: string) : string = jsNative
"""

// ============================================================
// Parameterized files (app name substitution)
// ============================================================

let private indexHtmlTmpl = """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{NAME}}</title>
    <link rel="stylesheet" href="/public/styles.css">
    <script src="/lib/guest-session.js"></script>
</head>
<body>
    <div id="app"></div>
    <script type="module" src="/dist/client/App.js"></script>
</body>
</html>
"""

let indexHtml (appName: string) = indexHtmlTmpl.Replace("{{NAME}}", toPascalCase appName)

let private adminHtmlTmpl = """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{NAME}} Admin</title>
    <link rel="stylesheet" href="/public/styles.css">
</head>
<body>
    <div id="app"></div>
    <script>
      var m = window.location.hash.match(/key=([^&]*)/);
      if (m) {
        localStorage.setItem('adminKey', decodeURIComponent(m[1]));
        history.replaceState(null, '', window.location.pathname);
      }
    </script>
    <script type="module" src="/dist/admin/App.js"></script>
</body>
</html>
"""

let adminHtml (appName: string) = adminHtmlTmpl.Replace("{{NAME}}", toPascalCase appName)

let private packageJsonTmpl = """{
  "name": "{{APP}}",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "concurrently -n client,server,gen -c blue,green,yellow \"npm run dev:client\" \"npm run dev:server\" \"npm run gen:watch\"",
    "dev:client": "concurrently -n fable,fable-admin,vite -c cyan,magenta,blue \"npm run fable:watch\" \"npm run fable:watch:admin\" \"vite\"",
    "dev:server": "concurrently -n fable-server,wrangler -c green,yellow \"npm run fable:watch:server\" \"wrangler dev\"",
    "build": "npm run build:client && npm run build:server",
    "build:client": "dotnet fable src/Client/Client.fsproj -o dist/client && vite build",
    "build:server": "dotnet fable src/Server/Server.fsproj -o dist/server",
    "deploy": "npm run build && wrangler deploy",
    "fable:watch": "dotnet fable watch src/Client/Client.fsproj -o dist/client",
    "fable:watch:server": "dotnet fable watch src/Server/Server.fsproj -o dist/server",
    "fable:watch:admin": "dotnet fable watch ../../packages/hedge/src/Admin/Admin.fsproj -o dist/admin",
    "gen": "dotnet run --project src/Gen/Gen.fsproj",
    "gen:watch": "dotnet watch run --project src/Gen/Gen.fsproj",
    "migrate": "dotnet run --project src/Gen/Gen.fsproj -- migrate",
    "migrate:dry": "dotnet run --project src/Gen/Gen.fsproj -- migrate --dry-run"
  },
  "devDependencies": {
    "@vitejs/plugin-react": "^4.3.0",
    "concurrently": "^8.2.2",
    "esbuild": "^0.24.0",
    "vite": "^5.4.0",
    "vite-plugin-fable": "0.0.31",
    "wrangler": "^4.67.0"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1"
  }
}
"""

let packageJson (appName: string) = packageJsonTmpl.Replace("{{APP}}", appName)

let private wranglerTomlTmpl = """name = "{{APP}}"
main = "worker-entry.js"
compatibility_date = "2024-01-01"
compatibility_flags = ["nodejs_compat"]

# Local development
[dev]
port = 8787
local_protocol = "http"

# D1 Database binding
[[d1_databases]]
binding = "DB"
database_name = "{{APP}}-db"
database_id = "local"  # Replace with real ID after `wrangler d1 create {{APP}}-db`

# Durable Objects
[durable_objects]
bindings = [
    { name = "EVENTS", class_name = "EventHub" }
]

[[migrations]]
tag = "v1"
new_classes = ["EventHub"]

# R2 blob storage
[[r2_buckets]]
binding = "BLOBS"
bucket_name = "{{APP}}-blobs"

# Environment variables
[vars]
ENVIRONMENT = "development"
ADMIN_KEY = "dev-admin-key"
"""

let wranglerToml (appName: string) = wranglerTomlTmpl.Replace("{{APP}}", appName)

// ============================================================
// Starter content (minimal working example)
// ============================================================

let domainFs = """module Models.Domain

open Hedge.Interface

type Post = {
    Id: PrimaryKey<string>
    Title: string
    Body: string
    CreatedAt: CreateTimestamp
}
"""

let apiFs = """module Models.Api

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
"""

let wsFs = """module Models.Ws

/// WebSocket event payloads.
/// Add event types here as needed.
"""

let configFs = """module Models.Config

/// Global configuration embedded per-host.
type GlobalConfig = {
    SiteName: string
}
"""

let private clientAppTmpl = """module Client.App

open Feliz
open Elmish

type Model = {
    Loading: bool
    Error: string option
}

type Msg =
    | NoOp

let init () =
    { Loading = false; Error = None }, Cmd.none

let update msg model =
    match msg with
    | NoOp -> model, Cmd.none

let view model dispatch =
    Html.div [
        prop.className "app"
        prop.children [
            Html.h1 "{{NAME}}"
            match model.Error with
            | Some err ->
                Html.div [ prop.className "error"; prop.text err ]
            | None -> Html.none
            Html.p "Edit src/Client/App.fs to get started."
        ]
    ]

open Elmish.React

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
"""

let clientAppFs (appName: string) = clientAppTmpl.Replace("{{NAME}}", toPascalCase appName)

let stylesCss = """/* Base styles */
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: system-ui, -apple-system, sans-serif; line-height: 1.5; }
.app { max-width: 800px; margin: 0 auto; padding: 1rem; }
header { border-bottom: 1px solid #eee; padding-bottom: 1rem; margin-bottom: 1rem; }
h1 { font-size: 1.5rem; }
.loading { color: #666; }
.error { background: #fee; border: 1px solid #fcc; padding: 0.5rem; border-radius: 4px; margin-bottom: 1rem; }
.feed-item { border-bottom: 1px solid #eee; padding: 1rem 0; }
.feed-item h2 { font-size: 1.1rem; margin-bottom: 0.25rem; }
"""

// ============================================================
// fsproj templates
// ============================================================

let genFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../../../packages/hedge/src/Gen/Program.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Models/Models.fsproj" />
  </ItemGroup>
</Project>
"""

let modelsFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Domain.fs" />
    <Compile Include="Ws.fs" />
    <Compile Include="Config.fs" />
    <Compile Include="Api.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
  </ItemGroup>
</Project>
"""

let codecsFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="generated/Codecs.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
    <ProjectReference Include="../Models/Models.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
    <PackageReference Include="Thoth.Json" Version="10.2.0" />
  </ItemGroup>
</Project>
"""

let serverFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Env.fs" />
    <Compile Include="generated/Db.fs" />
    <Compile Include="Handlers.fs" />
    <Compile Include="generated/AdminGen.fs" />
    <Compile Include="AdminConfig.fs" />
    <Compile Include="Admin.fs" />
    <Compile Include="generated/Routes.fs" />
    <Compile Include="EventHub.fs" />
    <Compile Include="Worker.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
    <ProjectReference Include="../Models/Models.fsproj" />
    <ProjectReference Include="../Codecs/Codecs.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
    <PackageReference Include="Fable.Promise" Version="3.2.0" />
    <PackageReference Include="Thoth.Json" Version="10.2.0" />
  </ItemGroup>
</Project>
"""

let clientFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="GuestSession.fs" />
    <Compile Include="Api.fs" />
    <Compile Include="generated/ClientGen.fs" />
    <Compile Include="RichText.fs" />
    <Compile Include="App.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
    <ProjectReference Include="../Models/Models.fsproj" />
    <ProjectReference Include="../Codecs/Codecs.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
    <PackageReference Include="Fable.Browser.Dom" Version="2.16.0" />
    <PackageReference Include="Feliz" Version="2.9.0" />
    <PackageReference Include="Feliz.Router" Version="4.0.0" />
    <PackageReference Include="Fable.Elmish" Version="4.2.0" />
    <PackageReference Include="Fable.Elmish.React" Version="4.0.0" />
    <PackageReference Include="Fable.Elmish.HMR" Version="7.0.0" />
    <PackageReference Include="Thoth.Fetch" Version="3.0.1" />
  </ItemGroup>
</Project>
"""

// ============================================================
// Main — scaffold command
// ============================================================

[<EntryPoint>]
let main (argv: string array) =
    if argv.Length = 0 then
        printfn "Usage: dotnet run --project packages/hedge/src/Gen/Scaffold.fsproj -- <app-name>"
        printfn ""
        printfn "Creates apps/<app-name>/ with a working Hedge app skeleton."
        1
    else
        let appName = argv.[0]
        let root = Path.Combine("apps", appName)

        if Directory.Exists root then
            printfn "ERROR: %s already exists" root
            1
        else
            printfn "Scaffolding %s..." root

            // Static files
            writeFile root ".nvmrc" nvmrc
            writeFile root "vite.config.js" viteConfigJs
            writeFile root "worker-entry.js" workerEntryJs
            writeFile root "lib/guest-session.js" guestSessionJs

            // Server static
            writeFile root "src/Server/Worker.fs" workerFs
            writeFile root "src/Server/Env.fs" envFs
            writeFile root "src/Server/Admin.fs" (fixTQ adminFs)
            writeFile root "src/Server/AdminConfig.fs" (fixTQ adminConfigFs)
            writeFile root "src/Server/EventHub.fs" (fixTQ eventHubFs)

            // Client static
            writeFile root "src/Client/Api.fs" (fixTQ clientApiFs)
            writeFile root "src/Client/GuestSession.fs" guestSessionFs
            writeFile root "src/Client/RichText.fs" richTextFs

            // Parameterized files
            writeFile root "index.html" (indexHtml appName)
            writeFile root "admin.html" (adminHtml appName)
            writeFile root "package.json" (packageJson appName)
            writeFile root "wrangler.toml" (wranglerToml appName)

            // Starter content
            writeFile root "src/Models/Domain.fs" domainFs
            writeFile root "src/Models/Api.fs" apiFs
            writeFile root "src/Models/Ws.fs" wsFs
            writeFile root "src/Models/Config.fs" configFs
            // Handlers.fs is generated by `npm run gen` on first run
            writeFile root "src/Client/App.fs" (clientAppFs appName)
            writeFile root "public/styles.css" stylesCss

            // fsproj templates
            writeFile root "src/Gen/Gen.fsproj" genFsproj
            writeFile root "src/Models/Models.fsproj" modelsFsproj
            writeFile root "src/Codecs/Codecs.fsproj" codecsFsproj
            writeFile root "src/Server/Server.fsproj" serverFsproj
            writeFile root "src/Client/Client.fsproj" clientFsproj

            // Empty generated dirs
            Directory.CreateDirectory(Path.Combine(root, "src/Server/generated")) |> ignore
            Directory.CreateDirectory(Path.Combine(root, "src/Client/generated")) |> ignore
            Directory.CreateDirectory(Path.Combine(root, "src/Codecs/generated")) |> ignore
            Directory.CreateDirectory(Path.Combine(root, "migrations")) |> ignore

            printfn ""
            printfn "Done! Next steps:"
            printfn "  cd %s" root
            printfn "  npm install"
            printfn "  npm run gen"
            printfn "  npm run dev"

            0
