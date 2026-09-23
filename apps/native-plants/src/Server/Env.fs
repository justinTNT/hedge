module Server.Env
open Hedge.Workers
type Env = { DB: D1Database; BLOBS: R2Bucket; ADMIN_KEY: string }
