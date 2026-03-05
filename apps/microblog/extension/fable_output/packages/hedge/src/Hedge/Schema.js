import { Record, Union } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, list_type, union_type, int32_type, string_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { substring, printf, toText } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { map, item } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { empty, ofArray } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { toArray } from "../../../../fable_modules/fable-library-js.4.29.0/Option.js";

export class FieldAttr extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["PrimaryKey", "CreateTimestamp", "UpdateTimestamp", "SoftDelete", "ForeignKey", "RichContent", "Link", "Required", "Trim", "Inject", "Unique", "MinLength", "MaxLength"];
    }
}

export function FieldAttr_$reflection() {
    return union_type("Hedge.Schema.FieldAttr", [], FieldAttr, () => [[], [], [], [], [["table", string_type]], [], [], [], [], [], [], [["Item", int32_type]], [["Item", int32_type]]]);
}

export class TypeAttr extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["HostAdmin", "ProjectAdmin"];
    }
}

export function TypeAttr_$reflection() {
    return union_type("Hedge.Schema.TypeAttr", [], TypeAttr, () => [[], []]);
}

export class FieldType extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["FString", "FInt", "FBool", "FOption", "FList", "FRecord"];
    }
}

export function FieldType_$reflection() {
    return union_type("Hedge.Schema.FieldType", [], FieldType, () => [[], [], [], [["Item", FieldType_$reflection()]], [["Item", FieldType_$reflection()]], [["name", string_type]]]);
}

export class FieldSchema extends Record {
    constructor(Name, Type, Attrs) {
        super();
        this.Name = Name;
        this.Type = Type;
        this.Attrs = Attrs;
    }
}

export function FieldSchema_$reflection() {
    return record_type("Hedge.Schema.FieldSchema", [], FieldSchema, () => [["Name", string_type], ["Type", FieldType_$reflection()], ["Attrs", list_type(FieldAttr_$reflection())]]);
}

export class TypeSchema extends Record {
    constructor(Name, Fields, Attrs) {
        super();
        this.Name = Name;
        this.Fields = Fields;
        this.Attrs = Attrs;
    }
}

export function TypeSchema_$reflection() {
    return record_type("Hedge.Schema.TypeSchema", [], TypeSchema, () => [["Name", string_type], ["Fields", list_type(FieldSchema_$reflection())], ["Attrs", list_type(TypeAttr_$reflection())]]);
}

export function showAttr(_arg) {
    switch (_arg.tag) {
        case 1:
            return "CreateTimestamp";
        case 2:
            return "UpdateTimestamp";
        case 3:
            return "SoftDelete";
        case 4:
            return toText(printf("ForeignKey(%s)"))(_arg.fields[0]);
        case 5:
            return "RichContent";
        case 6:
            return "Link";
        case 10:
            return "Unique";
        case 7:
            return "Required";
        case 8:
            return "Trim";
        case 9:
            return "Inject";
        case 11:
            return toText(printf("MinLength(%d)"))(_arg.fields[0]);
        case 12:
            return toText(printf("MaxLength(%d)"))(_arg.fields[0]);
        default:
            return "PrimaryKey";
    }
}

export function showFieldType(_arg) {
    switch (_arg.tag) {
        case 1:
            return "int";
        case 2:
            return "bool";
        case 3: {
            const arg = showFieldType(_arg.fields[0]);
            return toText(printf("%s option"))(arg);
        }
        case 4: {
            const arg_1 = showFieldType(_arg.fields[0]);
            return toText(printf("%s list"))(arg_1);
        }
        case 5:
            return _arg.fields[0];
        default:
            return "string";
    }
}

function classifyType(t) {
    let g, g_1, i, g_2, i_1;
    const fn = t.fullname;
    const gs = t.generics || [];
    if (fn.indexOf("Interface.PrimaryKey") >= 0) {
        return [(gs.length > 0) ? ((g = (item(0, gs).fullname), (g.indexOf("Int32") >= 0) ? (new FieldType(1, [])) : (new FieldType(0, [])))) : (new FieldType(0, [])), new FieldAttr(0, [])];
    }
    else if (fn.indexOf("Interface.CreateTimestamp") >= 0) {
        return [new FieldType(1, []), new FieldAttr(1, [])];
    }
    else if (fn.indexOf("Interface.UpdateTimestamp") >= 0) {
        return [new FieldType(1, []), new FieldAttr(2, [])];
    }
    else if (fn.indexOf("Interface.SoftDelete") >= 0) {
        return [new FieldType(1, []), new FieldAttr(3, [])];
    }
    else if (fn.indexOf("Interface.ForeignKey") >= 0) {
        return [new FieldType(0, []), new FieldAttr(4, [(gs.length > 0) ? ((g_1 = (item(0, gs).fullname), (i = (g_1.lastIndexOf(".") | 0), (i >= 0) ? substring(g_1, i + 1) : g_1))) : "?"])];
    }
    else if (fn.indexOf("Interface.RichContent") >= 0) {
        return [new FieldType(0, []), new FieldAttr(5, [])];
    }
    else if (fn.indexOf("Interface.Link") >= 0) {
        return [new FieldType(0, []), new FieldAttr(6, [])];
    }
    else if (fn.indexOf("Interface.Unique") >= 0) {
        return [(gs.length > 0) ? ((g_2 = (item(0, gs).fullname), (g_2.indexOf("Int32") >= 0) ? (new FieldType(1, [])) : (new FieldType(0, [])))) : (new FieldType(0, [])), new FieldAttr(10, [])];
    }
    else if (fn.indexOf("String") >= 0) {
        return [new FieldType(0, []), undefined];
    }
    else if (fn.indexOf("Int32") >= 0) {
        return [new FieldType(1, []), undefined];
    }
    else if (fn.indexOf("Boolean") >= 0) {
        return [new FieldType(2, []), undefined];
    }
    else if (fn.indexOf("FSharpList") >= 0) {
        return [new FieldType(4, [(gs.length > 0) ? classifyType(item(0, gs))[0] : (new FieldType(0, []))]), undefined];
    }
    else {
        return [new FieldType(5, [(i_1 = (fn.lastIndexOf(".") | 0), (i_1 >= 0) ? substring(fn, i_1 + 1) : fn)]), undefined];
    }
}

function classifyField(name, t) {
    const fn = t.fullname;
    const gs = t.generics || [];
    if ((fn.indexOf("FSharpOption") >= 0) && (gs.length > 0)) {
        const patternInput = classifyType(item(0, gs));
        return new FieldSchema(name, new FieldType(3, [patternInput[0]]), ofArray(toArray(patternInput[1])));
    }
    else {
        const patternInput_1 = classifyType(t);
        return new FieldSchema(name, patternInput_1[0], ofArray(toArray(patternInput_1[1])));
    }
}

/**
 * Derive a TypeSchema from a record type's reflection data.
 */
export function deriveSchema(name, t) {
    return new TypeSchema(name, ofArray(map((tupledArg) => classifyField(tupledArg[0], tupledArg[1]), t.fields ? t.fields() : [])), empty());
}

/**
 * Define a field with no attributes.
 */
export function field(name, ftype) {
    return new FieldSchema(name, ftype, empty());
}

/**
 * Define a field with attributes.
 */
export function fieldWith(name, ftype, attrs) {
    return new FieldSchema(name, ftype, attrs);
}

/**
 * Define a schema for a type.
 */
export function schema(name, fields) {
    return new TypeSchema(name, fields, empty());
}

/**
 * Define a schema with type-level attributes.
 */
export function schemaWith(name, attrs, fields) {
    return new TypeSchema(name, fields, attrs);
}

