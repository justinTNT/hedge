import { Attribute, Union } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { lambda_type, class_type, string_type, int32_type, union_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";

export class PrimaryKey$1 extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["PrimaryKey"];
    }
}

export function PrimaryKey$1_$reflection(gen0) {
    return union_type("Hedge.Interface.PrimaryKey`1", [gen0], PrimaryKey$1, () => [[["Item", gen0]]]);
}

export class CreateTimestamp extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["CreateTimestamp"];
    }
}

export function CreateTimestamp_$reflection() {
    return union_type("Hedge.Interface.CreateTimestamp", [], CreateTimestamp, () => [[["Item", int32_type]]]);
}

export class UpdateTimestamp extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["UpdateTimestamp"];
    }
}

export function UpdateTimestamp_$reflection() {
    return union_type("Hedge.Interface.UpdateTimestamp", [], UpdateTimestamp, () => [[["Item", int32_type]]]);
}

export class SoftDelete extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["SoftDelete"];
    }
}

export function SoftDelete_$reflection() {
    return union_type("Hedge.Interface.SoftDelete", [], SoftDelete, () => [[["Item", int32_type]]]);
}

export class ForeignKey$1 extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["ForeignKey"];
    }
}

export function ForeignKey$1_$reflection(gen0) {
    return union_type("Hedge.Interface.ForeignKey`1", [gen0], ForeignKey$1, () => [[["Item", string_type]]]);
}

export class RichContent extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["RichContent"];
    }
}

export function RichContent_$reflection() {
    return union_type("Hedge.Interface.RichContent", [], RichContent, () => [[["Item", string_type]]]);
}

export class Link extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["Link"];
    }
}

export function Link_$reflection() {
    return union_type("Hedge.Interface.Link", [], Link, () => [[["Item", string_type]]]);
}

export class Unique$1 extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["Unique"];
    }
}

export function Unique$1_$reflection(gen0) {
    return union_type("Hedge.Interface.Unique`1", [gen0], Unique$1, () => [[["Item", gen0]]]);
}

export class TableAttribute extends Attribute {
    constructor(name) {
        super();
        this.name = name;
    }
}

export function TableAttribute_$reflection() {
    return class_type("Hedge.Interface.TableAttribute", undefined, TableAttribute, class_type("System.Attribute"));
}

export function TableAttribute_$ctor_Z721C83C5(name) {
    return new TableAttribute(name);
}

export function TableAttribute__get_Name(_) {
    return _.name;
}

export class Get$1 extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["Get"];
    }
}

export function Get$1_$reflection(gen0) {
    return union_type("Hedge.Interface.Get`1", [gen0], Get$1, () => [[["Item", string_type]]]);
}

export class GetOne$1 extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["GetOne"];
    }
}

export function GetOne$1_$reflection(gen0) {
    return union_type("Hedge.Interface.GetOne`1", [gen0], GetOne$1, () => [[["Item", lambda_type(string_type, string_type)]]]);
}

export class Post$2 extends Union {
    constructor(Item) {
        super();
        this.tag = 0;
        this.fields = [Item];
    }
    cases() {
        return ["Post"];
    }
}

export function Post$2_$reflection(gen0, gen1) {
    return union_type("Hedge.Interface.Post`2", [gen0, gen1], Post$2, () => [[["Item", string_type]]]);
}

