module HedgeExtension.Chrome

open Fable.Core

// chrome.tabs.query({ active: true, currentWindow: true })
[<Emit("chrome.tabs.query({ active: true, currentWindow: true })")>]
let queryActiveTab () : JS.Promise<obj array> = jsNative

// chrome.scripting.executeScript({ target: { tabId }, func })
[<Emit("chrome.scripting.executeScript({ target: { tabId: $0 }, func: $1 })")>]
let executeScript (tabId: int) (func: unit -> obj) : JS.Promise<obj array> = jsNative

// chrome.runtime.sendMessage(msg)
[<Emit("chrome.runtime.sendMessage($0)")>]
let sendMessage (msg: obj) : JS.Promise<obj> = jsNative

// chrome.storage.local.get(keys)
[<Emit("chrome.storage.local.get($0)")>]
let storageGet (keys: string array) : JS.Promise<obj> = jsNative

// chrome.storage.local.set(data)
[<Emit("chrome.storage.local.set($0)")>]
let storageSet (data: obj) : JS.Promise<unit> = jsNative

// chrome.runtime.getURL(path)
[<Emit("chrome.runtime.getURL($0)")>]
let getURL (path: string) : string = jsNative
