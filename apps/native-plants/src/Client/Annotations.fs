module Client.Annotations

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open NativePlants.Glossary
open Hedge.Interface

[<Emit("(()=>{const r=$0.getBoundingClientRect(),w=Math.min(380,window.innerWidth-24),above=window.innerHeight-r.bottom<220 && r.top>r.bottom-r.top;return [Math.max(12,Math.min(r.left,window.innerWidth-w-12)),above?r.top:r.bottom,w,above,Math.max(100,(above?r.top:window.innerHeight-r.bottom)-12)];})()")>]
let private position (target:obj) : float*float*float*bool*float = jsNative
// `$1` is captured under its own name first: Fable substitutes the caller's identifier textually, so a
// local named like the F# argument (`close`) would shadow it and the handler would call itself.
[<Emit("(()=>{const dismiss=$1;const handler=e=>{if(e.type==='keydown'){if(e.key==='Escape')dismiss();}else if(e.type==='resize'||!$0.parentElement?.contains(e.target))dismiss();};window.addEventListener('keydown',handler);window.addEventListener('pointerdown',handler);window.addEventListener('scroll',handler,true);window.addEventListener('resize',handler);return ()=>{window.removeEventListener('keydown',handler);window.removeEventListener('pointerdown',handler);window.removeEventListener('scroll',handler,true);window.removeEventListener('resize',handler);};})()")>]
let private watch (target:obj) (close:unit->unit) : unit->unit = jsNative

[<ReactComponent>]
let Term (note:Note) (label:string) =
    let visible,setVisible=React.useState(false)
    let pinned,setPinned=React.useState(false)
    let placement,setPlacement=React.useState((12.,0.,320.,false,300.))
    let anchor=React.useRef<obj>(null)
    let id=React.useRef("definition-"+Guid.NewGuid().ToString("N"))
    let close () = setVisible false;setPinned false
    let show target = anchor.current<-target;setPlacement(position target);setVisible true
    React.useEffect((fun () ->
        let remove=if visible then watch anchor.current close else ignore
        {new IDisposable with member _.Dispose()=remove()}),[|box visible|])
    let left,top,width,above,maxHeight=placement
    Html.span [prop.className "annotated-term";prop.onMouseLeave(fun _->if not pinned then setVisible false);prop.children [
        Html.button [
            prop.type' "button";prop.className "term-button";prop.text label
            prop.ariaLabel(label+" — "+note.Heading);prop.ariaExpanded visible
            if visible then prop.custom("aria-describedby",id.current)
            prop.onMouseEnter(fun e -> if e.buttons=0 then show (box e.currentTarget))
            prop.onFocus(fun e -> show (box e.currentTarget))
            prop.onBlur(fun _ -> if not pinned then setVisible false)
            prop.onClick(fun e ->
                e.stopPropagation()
                if pinned then close()
                else
                    show (box e.currentTarget)
                    setPinned true)
            prop.onKeyDown(fun e -> if e.key="Escape" then e.preventDefault();e.stopPropagation();close())
        ]
        if visible then
            // A hover preview is a tooltip; a pinned definition (every touch open) is a small dialog
            // with its own close control, in addition to Escape, scrolling or a tap outside.
            Html.span [prop.id id.current;prop.role (if pinned then "dialog" else "tooltip");prop.className "definition-popover"
                       if pinned then prop.ariaLabel note.Heading
                       prop.style [
                           style.left(int left);style.top(int top);style.width(int width);style.maxHeight(int maxHeight)
                           if above then style.transform [transform.translateY(length.percent -100)]
                       ]
                       prop.children [
                           if pinned then
                               Html.button [prop.type' "button";prop.className "definition-close";prop.ariaLabel "Close definition";prop.text "×"
                                            prop.onClick(fun e -> e.stopPropagation();close())]
                           Html.strong note.Heading
                           Html.span [prop.className "definition-body";prop.text note.Body]
                           if note.Image<>Image "" then Client.Images.View note.Image [prop.alt(note.Heading+" illustration")]
                           Html.small note.Source
                       ]]
    ]]

let chunks parts =
    Html.span [prop.children [
        for part in parts do
            match part.Note with
            | Some note -> Term note part.Text
            | None -> Html.text part.Text
    ]]
let text matcher value = annotate matcher value |> chunks
let usage matcher references value = usageCitations matcher references value |> chunks
