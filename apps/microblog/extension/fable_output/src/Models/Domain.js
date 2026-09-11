import { Record } from "../../fable_modules/fable-library-js.4.29.0/Types.js";
import { int32_type, record_type, option_type, string_type } from "../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { ForeignKey$1_$reflection, SoftDelete_$reflection, CreateTimestamp_$reflection, PrimaryKey$1_$reflection } from "../../packages/hedge/src/Hedge/Interface.js";

export class Guest extends Record {
    constructor(Id, SessionId, CreatedAt, DeletedAt) {
        super();
        this.Id = Id;
        this.SessionId = SessionId;
        this.CreatedAt = CreatedAt;
        this.DeletedAt = DeletedAt;
    }
}

export function Guest_$reflection() {
    return record_type("Models.Domain.Guest", [], Guest, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["SessionId", string_type], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class Identity extends Record {
    constructor(Id, GuestId, Provider, ProviderUserId, Name, Picture, Email, ActivatedAt, CreatedAt) {
        super();
        this.Id = Id;
        this.GuestId = GuestId;
        this.Provider = Provider;
        this.ProviderUserId = ProviderUserId;
        this.Name = Name;
        this.Picture = Picture;
        this.Email = Email;
        this.ActivatedAt = ActivatedAt;
        this.CreatedAt = CreatedAt;
    }
}

export function Identity_$reflection() {
    return record_type("Models.Domain.Identity", [], Identity, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["GuestId", ForeignKey$1_$reflection(Guest_$reflection())], ["Provider", string_type], ["ProviderUserId", string_type], ["Name", string_type], ["Picture", string_type], ["Email", option_type(string_type)], ["ActivatedAt", option_type(int32_type)], ["CreatedAt", CreateTimestamp_$reflection()]]);
}

