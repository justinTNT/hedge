// The static admin shell uses the same generated capability client and credential
// subscription as the main app. No independent JSON decoder or storage polling.
export { storedKey as storedAdminKey, readCapabilities, listen as subscribeCredentials } from '../../dist/client/Access.js';
