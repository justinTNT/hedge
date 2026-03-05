import { list, object } from "../../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";
import { ofArray, map } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { list as list_1, object as object_1, int, oneOf, field, map as map_1, string, fail, succeed, andThen } from "../../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { uncurry2, curry2, uncurry3 } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { TypeSchema, FieldSchema, TypeAttr, FieldAttr, FieldType } from "./Schema.js";
import { printf, toText } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";

export function encodeFieldType(ft) {
    switch (ft.tag) {
        case 1:
            return "int";
        case 2:
            return "bool";
        case 3:
            return object([["option", encodeFieldType(ft.fields[0])]]);
        case 4:
            return object([["list", encodeFieldType(ft.fields[0])]]);
        case 5:
            return object([["record", ft.fields[0]]]);
        default:
            return "string";
    }
}

export function encodeFieldAttr(fa) {
    switch (fa.tag) {
        case 1:
            return "createTimestamp";
        case 2:
            return "updateTimestamp";
        case 3:
            return "softDelete";
        case 4:
            return object([["foreignKey", fa.fields[0]]]);
        case 5:
            return "richContent";
        case 6:
            return "link";
        case 10:
            return "unique";
        case 7:
            return "required";
        case 8:
            return "trim";
        case 9:
            return "inject";
        case 11:
            return object([["minLength", fa.fields[0]]]);
        case 12:
            return object([["maxLength", fa.fields[0]]]);
        default:
            return "primaryKey";
    }
}

export function encodeTypeAttr(ta) {
    if (ta.tag === 1) {
        return "projectAdmin";
    }
    else {
        return "hostAdmin";
    }
}

export function encodeFieldSchema(fs) {
    return object([["name", fs.Name], ["type", encodeFieldType(fs.Type)], ["attrs", list(map(encodeFieldAttr, fs.Attrs))]]);
}

export function encodeTypeSchema(ts) {
    return object([["name", ts.Name], ["fields", list(map(encodeFieldSchema, ts.Fields))], ["attrs", list(map(encodeTypeAttr, ts.Attrs))]]);
}

export const decodeFieldType = (() => {
    const inner = (path, value) => {
        const matchValue = andThen(uncurry3((s) => {
            switch (s) {
                case "string":
                    return (arg10$0040) => ((arg20$0040) => succeed(new FieldType(0, []), arg10$0040, arg20$0040));
                case "int":
                    return (arg10$0040_1) => ((arg20$0040_1) => succeed(new FieldType(1, []), arg10$0040_1, arg20$0040_1));
                case "bool":
                    return (arg10$0040_2) => ((arg20$0040_2) => succeed(new FieldType(2, []), arg10$0040_2, arg20$0040_2));
                default: {
                    const msg = toText(printf("Unknown field type: %s"))(s);
                    return (path_2) => ((arg20$0040_3) => fail(msg, path_2, arg20$0040_3));
                }
            }
        }), string, path, value);
        if (matchValue.tag === 1) {
            const matchValue_1 = map_1((Item) => (new FieldType(3, [Item])), (path_4, value_3) => field("option", inner, path_4, value_3), path, value);
            if (matchValue_1.tag === 1) {
                const matchValue_2 = map_1((Item_1) => (new FieldType(4, [Item_1])), (path_6, value_5) => field("list", inner, path_6, value_5), path, value);
                if (matchValue_2.tag === 1) {
                    return map_1((name) => (new FieldType(5, [name])), (path_9, value_8) => field("record", string, path_9, value_8), path, value);
                }
                else {
                    return matchValue_2;
                }
            }
            else {
                return matchValue_1;
            }
        }
        else {
            return matchValue;
        }
    };
    return curry2(inner);
})();

export const decodeFieldAttr = (path_10) => ((value_9) => oneOf(ofArray([(path_2) => ((value_1) => andThen(uncurry3((s) => {
    switch (s) {
        case "primaryKey":
            return (arg10$0040) => ((arg20$0040) => succeed(new FieldAttr(0, []), arg10$0040, arg20$0040));
        case "createTimestamp":
            return (arg10$0040_1) => ((arg20$0040_1) => succeed(new FieldAttr(1, []), arg10$0040_1, arg20$0040_1));
        case "updateTimestamp":
            return (arg10$0040_2) => ((arg20$0040_2) => succeed(new FieldAttr(2, []), arg10$0040_2, arg20$0040_2));
        case "softDelete":
            return (arg10$0040_3) => ((arg20$0040_3) => succeed(new FieldAttr(3, []), arg10$0040_3, arg20$0040_3));
        case "richContent":
            return (arg10$0040_4) => ((arg20$0040_4) => succeed(new FieldAttr(5, []), arg10$0040_4, arg20$0040_4));
        case "link":
            return (arg10$0040_5) => ((arg20$0040_5) => succeed(new FieldAttr(6, []), arg10$0040_5, arg20$0040_5));
        case "unique":
            return (arg10$0040_6) => ((arg20$0040_6) => succeed(new FieldAttr(10, []), arg10$0040_6, arg20$0040_6));
        case "required":
            return (arg10$0040_7) => ((arg20$0040_7) => succeed(new FieldAttr(7, []), arg10$0040_7, arg20$0040_7));
        case "trim":
            return (arg10$0040_8) => ((arg20$0040_8) => succeed(new FieldAttr(8, []), arg10$0040_8, arg20$0040_8));
        case "inject":
            return (arg10$0040_9) => ((arg20$0040_9) => succeed(new FieldAttr(9, []), arg10$0040_9, arg20$0040_9));
        default: {
            const msg = toText(printf("Unknown field attr: %s"))(s);
            return (path_1) => ((arg20$0040_10) => fail(msg, path_1, arg20$0040_10));
        }
    }
}), string, path_2, value_1)), (path_5) => ((value_4) => map_1((table) => (new FieldAttr(4, [table])), (path_4, value_3) => field("foreignKey", string, path_4, value_3), path_5, value_4)), (path_7) => ((value_6) => map_1((Item) => (new FieldAttr(11, [Item])), (path_6, value_5) => field("minLength", uncurry2(int), path_6, value_5), path_7, value_6)), (path_9) => ((value_8) => map_1((Item_1) => (new FieldAttr(12, [Item_1])), (path_8, value_7) => field("maxLength", uncurry2(int), path_8, value_7), path_9, value_8))]), path_10, value_9));

export const decodeTypeAttr = (path_2) => ((value_1) => andThen(uncurry3((s) => {
    switch (s) {
        case "hostAdmin":
            return (arg10$0040) => ((arg20$0040) => succeed(new TypeAttr(0, []), arg10$0040, arg20$0040));
        case "projectAdmin":
            return (arg10$0040_1) => ((arg20$0040_1) => succeed(new TypeAttr(1, []), arg10$0040_1, arg20$0040_1));
        default: {
            const msg = toText(printf("Unknown type attr: %s"))(s);
            return (path_1) => ((arg20$0040_2) => fail(msg, path_1, arg20$0040_2));
        }
    }
}), string, path_2, value_1));

export const decodeFieldSchema = (path_2) => ((v) => object_1((get$) => {
    let objectArg, objectArg_1, objectArg_2;
    return new FieldSchema((objectArg = get$.Required, objectArg.Field("name", string)), (objectArg_1 = get$.Required, objectArg_1.Field("type", uncurry2(decodeFieldType))), (objectArg_2 = get$.Required, objectArg_2.Field("attrs", (path_1, value_1) => list_1(uncurry2(decodeFieldAttr), path_1, value_1))));
}, path_2, v));

export const decodeTypeSchema = (path_3) => ((v) => object_1((get$) => {
    let objectArg, objectArg_1, objectArg_2;
    return new TypeSchema((objectArg = get$.Required, objectArg.Field("name", string)), (objectArg_1 = get$.Required, objectArg_1.Field("fields", (path_1, value_1) => list_1(uncurry2(decodeFieldSchema), path_1, value_1))), (objectArg_2 = get$.Required, objectArg_2.Field("attrs", (path_2, value_2) => list_1(uncurry2(decodeTypeAttr), path_2, value_2))));
}, path_3, v));

