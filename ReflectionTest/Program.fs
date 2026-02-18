module ReflectionTest

open System
open Fable.Core
open Fable.Core.Reflection

// 1. Define a Custom Attribute
// In Fable, inheriting from System.Attribute is standard, but sometimes requires specific compilation handling.
type AdminLabelAttribute(label: string) =
    inherit Attribute()
    member _.Label = label

// 2. Define a Record with Metadata
// The test is: Does "User Name" survive the trip to JavaScript?
type User = {
    [<AdminLabel("User Name")>]
    Name: string
    
    [<AdminLabel("User Email")>]
    Email: string
}

[<EntryPoint>]
let main argv =
    printfn "Starting Reflection Test..."

    // 3. Reflect on the Type
    // FSharpType.GetRecordFields is the standard F# reflection API.
    // In .NET this reads metadata bytes. In Fable, this reads generated JS objects.
    let t = typeof<User>
    let fields = FSharpType.GetRecordFields(t)

    // 4. Inspect Fields for Attributes
    let mutable successCount = 0
    
    for field in fields do
        let attributes = field.GetCustomAttributes(typeof<AdminLabelAttribute>, false)
        if attributes.Length > 0 then
            let attr = attributes.[0] :?> AdminLabelAttribute
            printfn "SUCCESS: Field '%s' has label: '%s'" field.Name attr.Label
            successCount <- successCount + 1
        else
            printfn "FAILURE: Field '%s' has NO attributes" field.Name

    if successCount = 2 then
        printfn "VERDICT: PASS - Custom Attributes are preserved."
        0
    else
        printfn "VERDICT: FAIL - Attributes were erased."
        1
