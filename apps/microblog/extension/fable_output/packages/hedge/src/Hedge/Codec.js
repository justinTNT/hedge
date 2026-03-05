import { map as map_1, item } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { nil, object, list as list_1 } from "../../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";
import { ofArray, map } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { object as object_1, list as list_2, bool, int, string, map as map_2 } from "../../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { uncurry2 } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { map as map_3 } from "../../../../fable_modules/fable-library-js.4.29.0/Option.js";

function camelCase(s) {
    if (s.length === 0) {
        return s;
    }
    else {
        return s[0].toLowerCase() + s.slice(1, s.length);
    }
}

function wrapperBaseType(fullname, generics) {
    if (fullname.indexOf("Interface.PrimaryKey") >= 0) {
        if ((generics.length > 0) && ((item(0, generics).fullname).indexOf("Int32") >= 0)) {
            return "int";
        }
        else {
            return "string";
        }
    }
    else if (fullname.indexOf("Interface.CreateTimestamp") >= 0) {
        return "int";
    }
    else if (fullname.indexOf("Interface.UpdateTimestamp") >= 0) {
        return "int";
    }
    else if (fullname.indexOf("Interface.SoftDelete") >= 0) {
        return "int";
    }
    else if (fullname.indexOf("Interface.ForeignKey") >= 0) {
        return "string";
    }
    else if (fullname.indexOf("Interface.RichContent") >= 0) {
        return "string";
    }
    else if (fullname.indexOf("Interface.Link") >= 0) {
        return "string";
    }
    else if (fullname.indexOf("Interface.Unique") >= 0) {
        if ((generics.length > 0) && ((item(0, generics).fullname).indexOf("Int32") >= 0)) {
            return "int";
        }
        else {
            return "string";
        }
    }
    else {
        return undefined;
    }
}

export function encodeValue(typeInfo, value) {
    const fn = typeInfo.fullname;
    const gs = typeInfo.generics || [];
    const matchValue = wrapperBaseType(fn, gs);
    if (matchValue == null) {
        if (fn.indexOf("String") >= 0) {
            return value;
        }
        else if (fn.indexOf("Int32") >= 0) {
            return value;
        }
        else if (fn.indexOf("Boolean") >= 0) {
            return value;
        }
        else if (fn.indexOf("FSharpList") >= 0) {
            const innerTi = (gs.length > 0) ? item(0, gs) : typeInfo;
            return list_1(map((value_6) => encodeValue(innerTi, value_6), value));
        }
        else {
            return encodeRecord(typeInfo, value);
        }
    }
    else if (matchValue === "int") {
        return value.fields[0];
    }
    else {
        return value.fields[0];
    }
}

export function encodeRecord(typeInfo, value) {
    return object(ofArray(map_1((tupledArg) => {
        const name = tupledArg[0];
        const fieldTi = tupledArg[1];
        const jsonName = camelCase(name);
        const fieldVal = value[name];
        const fn = fieldTi.fullname;
        const gs = fieldTi.generics || [];
        if ((fn.indexOf("FSharpOption") >= 0) && (gs.length > 0)) {
            const innerTi = item(0, gs);
            if ((fieldVal == null)) {
                return [jsonName, nil];
            }
            else {
                return [jsonName, encodeValue(innerTi, fieldVal)];
            }
        }
        else {
            return [jsonName, encodeValue(fieldTi, fieldVal)];
        }
    }, typeInfo.fields ? typeInfo.fields() : [])));
}

export function decoderFor(typeInfo) {
    const fn = typeInfo.fullname;
    const gs = typeInfo.generics || [];
    const matchValue = wrapperBaseType(fn, gs);
    if (matchValue == null) {
        if (fn.indexOf("String") >= 0) {
            return (path_4) => ((value_5) => map_2((value_4) => value_4, string, path_4, value_5));
        }
        else if (fn.indexOf("Int32") >= 0) {
            return (path_5) => ((value_7) => map_2((value_6) => value_6, uncurry2(int), path_5, value_7));
        }
        else if (fn.indexOf("Boolean") >= 0) {
            return (path_7) => ((value_10) => map_2((value_9) => value_9, bool, path_7, value_10));
        }
        else if (fn.indexOf("FSharpList") >= 0) {
            let d_5;
            const decoder = decoderFor((gs.length > 0) ? item(0, gs) : typeInfo);
            d_5 = ((path_8) => ((value_11) => list_2(uncurry2(decoder), path_8, value_11)));
            return (path_9) => ((value_13) => map_2((value_12) => value_12, uncurry2(d_5), path_9, value_13));
        }
        else {
            return decodeRecordObj(typeInfo);
        }
    }
    else if (matchValue === "int") {
        return (path) => ((value) => map_2((i) => (new (typeInfo.construct)(i)), uncurry2(int), path, value));
    }
    else {
        return (path_2) => ((value_2) => map_2((s) => (new (typeInfo.construct)(s)), string, path_2, value_2));
    }
}

export function decodeRecordObj(typeInfo) {
    return (path) => ((v) => object_1((get$) => {
        const values = map_1((tupledArg) => decodeFieldValue(get$, camelCase(tupledArg[0]), tupledArg[1]), typeInfo.fields ? typeInfo.fields() : []);
        return new (typeInfo.construct)(...values);
    }, path, v));
}

export function decodeFieldValue(get$, jsonName, fieldTi) {
    let objectArg_2, objectArg_3, objectArg_4, objectArg_5, objectArg_6, objectArg, objectArg_1;
    const fn = fieldTi.fullname;
    const gs = fieldTi.generics || [];
    if ((fn.indexOf("FSharpOption") >= 0) && (gs.length > 0)) {
        const innerTi = item(0, gs);
        const innerFn = innerTi.fullname;
        const innerGs = innerTi.generics || [];
        const matchValue = wrapperBaseType(innerFn, innerGs);
        if (matchValue == null) {
            if (innerFn.indexOf("String") >= 0) {
                return (objectArg_2 = get$.Optional, objectArg_2.Field(jsonName, string));
            }
            else if (innerFn.indexOf("Int32") >= 0) {
                return (objectArg_3 = get$.Optional, objectArg_3.Field(jsonName, uncurry2(int)));
            }
            else if (innerFn.indexOf("Boolean") >= 0) {
                return (objectArg_4 = get$.Optional, objectArg_4.Field(jsonName, bool));
            }
            else if (innerFn.indexOf("FSharpList") >= 0) {
                let dec;
                const decoder = decoderFor((innerGs.length > 0) ? item(0, innerGs) : innerTi);
                dec = ((path_3) => ((value_3) => list_2(uncurry2(decoder), path_3, value_3)));
                return (objectArg_5 = get$.Optional, objectArg_5.Field(jsonName, uncurry2(dec)));
            }
            else {
                const dec_1 = decodeRecordObj(innerTi);
                return (objectArg_6 = get$.Optional, objectArg_6.Field(jsonName, uncurry2(dec_1)));
            }
        }
        else if (matchValue === "int") {
            return map_3((i) => (new (innerTi.construct)(i)), (objectArg = get$.Optional, objectArg.Field(jsonName, uncurry2(int))));
        }
        else {
            return map_3((s) => (new (innerTi.construct)(s)), (objectArg_1 = get$.Optional, objectArg_1.Field(jsonName, string)));
        }
    }
    else {
        const dec_2 = decoderFor(fieldTi);
        const objectArg_7 = get$.Required;
        return objectArg_7.Field(jsonName, uncurry2(dec_2));
    }
}

