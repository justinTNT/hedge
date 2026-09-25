module Client.AdminCredential

open Fable.Core

[<Emit("localStorage.getItem('adminKey') || ''")>]
let private rawRead () : string = jsNative

/// A saved key is only a credential candidate, never confirmed permission.
let read () = try rawRead () with _ -> ""

[<Emit("(($0 === '' ? localStorage.removeItem('adminKey') : localStorage.setItem('adminKey', $0)), window.dispatchEvent(new Event('hedge:admin-credential-changed')), undefined)")>]
let private rawWrite (key:string) : unit = jsNative

/// Notify the current document after successful storage, without putting credentials in the event.
let write key =
    try rawWrite key; Ok ()
    with _ -> Error "The admin key could not be saved in this browser."

let clear () = write ""

[<Emit("""((changed) => {
  const storage = e => { if (e.key === null || e.key === 'adminKey') changed(); };
  window.addEventListener('hedge:admin-credential-changed', changed);
  window.addEventListener('storage', storage);
  return () => {
    window.removeEventListener('hedge:admin-credential-changed', changed);
    window.removeEventListener('storage', storage);
  };
})($0)""")>]
let subscribe (changed:unit -> unit) : (unit -> unit) = jsNative
