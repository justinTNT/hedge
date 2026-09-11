import { Record } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { lambda_type, obj_type, record_type, option_type, string_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { comparePrimitives, int32ToString } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { ofList } from "../../../../fable_modules/fable-library-js.4.29.0/Map.js";
import { ofArray } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { printf, toText } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { promise } from "../../../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { hmacSha256 } from "./Workers.js";
import { FSharpResult$2 } from "../../../../fable_modules/fable-library-js.4.29.0/Result.js";
import { item } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { parse } from "../../../../fable_modules/fable-library-js.4.29.0/Int32.js";

export class UserInfo extends Record {
    constructor(Name, PictureUrl, Email, ProviderUserId, Provider) {
        super();
        this.Name = Name;
        this.PictureUrl = PictureUrl;
        this.Email = Email;
        this.ProviderUserId = ProviderUserId;
        this.Provider = Provider;
    }
}

export function UserInfo_$reflection() {
    return record_type("Hedge.OAuth.UserInfo", [], UserInfo, () => [["Name", string_type], ["PictureUrl", string_type], ["Email", option_type(string_type)], ["ProviderUserId", string_type], ["Provider", string_type]]);
}

export class ProviderConfig extends Record {
    constructor(AuthorizeUrl, TokenUrl, UserinfoUrl, Scopes, ParseUserinfo) {
        super();
        this.AuthorizeUrl = AuthorizeUrl;
        this.TokenUrl = TokenUrl;
        this.UserinfoUrl = UserinfoUrl;
        this.Scopes = Scopes;
        this.ParseUserinfo = ParseUserinfo;
    }
}

export function ProviderConfig_$reflection() {
    return record_type("Hedge.OAuth.ProviderConfig", [], ProviderConfig, () => [["AuthorizeUrl", string_type], ["TokenUrl", string_type], ["UserinfoUrl", string_type], ["Scopes", string_type], ["ParseUserinfo", lambda_type(obj_type, UserInfo_$reflection())]]);
}

const googleConfig = new ProviderConfig("https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://www.googleapis.com/oauth2/v2/userinfo", "openid profile email", (o) => {
    let e;
    return new UserInfo(o.name, o.picture, (e = o.email, (e == null) ? undefined : e), o.id, "google");
});

const githubConfig = new ProviderConfig("https://github.com/login/oauth/authorize", "https://github.com/login/oauth/access_token", "https://api.github.com/user", "read:user user:email", (o) => {
    let n, e;
    return new UserInfo((n = o.name, (n == null) ? o.login : n), o.avatar_url, (e = o.email, (e == null) ? undefined : e), int32ToString(o.id), "github");
});

const microsoftConfig = new ProviderConfig("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", "https://graph.microsoft.com/v1.0/me", "openid profile email", (o) => {
    let e;
    return new UserInfo(o.displayName, "", (e = o.mail, (e == null) ? undefined : e), o.id, "microsoft");
});

const facebookConfig = new ProviderConfig("https://www.facebook.com/v18.0/dialog/oauth", "https://graph.facebook.com/v18.0/oauth/access_token", "https://graph.facebook.com/me?fields=id,name,picture.type(large),email", "public_profile email", (o) => {
    let pic, data, e;
    return new UserInfo(o.name, (pic = o.picture, (pic == null) ? "" : ((data = pic.data, (data == null) ? "" : data.url))), (e = o.email, (e == null) ? undefined : e), o.id, "facebook");
});

export const providers = ofList(ofArray([["google", googleConfig], ["github", githubConfig], ["microsoft", microsoftConfig], ["facebook", facebookConfig]]), {
    Compare: comparePrimitives,
});

/**
 * Generate a signed state token: base64url(timestamp|guestId|returnTo|hmac)
 */
export function generateState(secret, guestId, returnTo) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const timestamp = int32ToString(Math.floor(Date.now() / 1000));
        const payload = toText(printf("%s|%s|%s"))(timestamp)(guestId)(returnTo);
        return hmacSha256(secret, payload).then((_arg) => {
            const token = toText(printf("%s|%s"))(payload)(_arg);
            return Promise.resolve(btoa(token).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, ''));
        });
    }));
}

/**
 * Verify a signed state token. Returns Ok (guestId, returnTo) or Error message.
 */
export function verifyState(secret, state) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const decoded = atob(state.replace(/-/g, '+').replace(/_/g, '/'));
        const parts = decoded.split("|");
        if (parts.length < 4) {
            return Promise.resolve(new FSharpResult$2(1, ["Invalid state"]));
        }
        else {
            const timestamp = item(0, parts);
            const guestId = item(1, parts);
            const returnTo = item(2, parts);
            const receivedHmac = item(3, parts);
            const payload = toText(printf("%s|%s|%s"))(timestamp)(guestId)(returnTo);
            return hmacSha256(secret, payload).then((_arg) => {
                if (receivedHmac !== _arg) {
                    return Promise.resolve(new FSharpResult$2(1, ["Invalid state signature"]));
                }
                else {
                    const ts = parse(timestamp, 511, false, 32) | 0;
                    return (((Math.floor(Date.now() / 1000)) - ts) > 900) ? (Promise.resolve(new FSharpResult$2(1, ["State token expired"]))) : (Promise.resolve(new FSharpResult$2(0, [[guestId, returnTo]])));
                }
            });
        }
    }).catch((_arg_1) => (Promise.resolve(new FSharpResult$2(1, ["Invalid state"])))))));
}

/**
 * Build the authorization redirect URL for a provider.
 */
export function generateAuthUrl(provider, clientId, redirectUri, state) {
    const arg_1 = encodeURIComponent(clientId);
    const arg_2 = encodeURIComponent(redirectUri);
    const arg_3 = encodeURIComponent(provider.Scopes);
    const arg_4 = encodeURIComponent(state);
    return toText(printf("%s?client_id=%s&redirect_uri=%s&scope=%s&state=%s&response_type=code"))(provider.AuthorizeUrl)(arg_1)(arg_2)(arg_3)(arg_4);
}

/**
 * Exchange an authorization code for an access token.
 */
export function exchangeCode(provider, code, redirectUri, clientId, clientSecret) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        let body;
        const arg = encodeURIComponent(code);
        const arg_1 = encodeURIComponent(redirectUri);
        const arg_2 = encodeURIComponent(clientId);
        const arg_3 = encodeURIComponent(clientSecret);
        body = toText(printf("grant_type=authorization_code&code=%s&redirect_uri=%s&client_id=%s&client_secret=%s"))(arg)(arg_1)(arg_2)(arg_3);
        const options = {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded",
                Accept: "application/json",
            },
            body: body,
        };
        return (fetch(provider.TokenUrl, options)).then((_arg) => {
            const response = _arg;
            return !response.ok ? ((response.text()).then((_arg_1) => (Promise.resolve((() => {
                let arg_4;
                throw new Error((arg_4 = (response.status | 0), toText(printf("Token exchange failed (%d): %s"))(arg_4)(_arg_1)));
            })())))) : ((response.json()).then((_arg_2) => {
                const token = _arg_2.access_token;
                return ((token == null) ? true : (token === "")) ? (Promise.resolve((() => {
                    throw new Error(toText(printf("No access_token in response")));
                })())) : (Promise.resolve(token));
            }));
        });
    }));
}

/**
 * Fetch and normalize user info from the provider.
 */
export function fetchUserinfo(provider, accessToken) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const options = {
            headers: {
                Authorization: toText(printf("Bearer %s"))(accessToken),
                Accept: "application/json",
                "User-Agent": "Hedge",
            },
        };
        return (fetch(provider.UserinfoUrl, options)).then((_arg) => {
            const response = _arg;
            return !response.ok ? ((response.text()).then((_arg_1) => (Promise.resolve((() => {
                let arg_1;
                throw new Error((arg_1 = (response.status | 0), toText(printf("Userinfo fetch failed (%d): %s"))(arg_1)(_arg_1)));
            })())))) : ((response.json()).then((_arg_2) => (Promise.resolve(provider.ParseUserinfo(_arg_2)))));
        });
    }));
}

