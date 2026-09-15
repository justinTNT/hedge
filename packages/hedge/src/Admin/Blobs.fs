module Admin.Blobs

open Fable.Core

/// Upload the first file selected in a file `<input>` to the existing admin-gated
/// `/api/blobs` endpoint (FormData field "file", `X-Admin-Key` from localStorage — the
/// same contract the rich-text uploader uses) and invoke `onDone` with the returned
/// `/blobs/<key>` URL. The file extraction lives in JS so the F# view stays typing-light;
/// best-effort — logs and no-ops on failure, and clears the input so re-picking the same
/// file fires change again.
[<Emit("""(function(inputEl, cb){
  var file = inputEl && inputEl.files && inputEl.files[0];
  if (!file) return;
  var fd = new FormData();
  fd.append('file', file);
  fetch('/api/blobs', { method: 'POST', headers: { 'X-Admin-Key': (localStorage.getItem('adminKey') || '') }, body: fd })
    .then(function(r){ if (!r.ok) throw new Error('upload failed: ' + r.status); return r.json(); })
    .then(function(j){ cb(j.url); try { inputEl.value = ''; } catch (e) {} })
    .catch(function(e){ console.error('[admin] image upload failed', e); });
})($0, $1)""")>]
let uploadFromInput (inputEl: obj) (onDone: string -> unit) : unit = jsNative
