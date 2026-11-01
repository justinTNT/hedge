import {Capacitor} from '@capacitor/core';
import {App} from '@capacitor/app';
import {Browser} from '@capacitor/browser';
import {SecureStoragePlugin as store} from 'capacitor-secure-storage-plugin';
import {previewFetch} from './transport.mjs';
import './mobile.css';

window.API_ORIGIN=__PLANTS_API_ORIGIN__;
window.HEDGE_MOBILE_RETURN_URL='nativeplants://auth';
const nativeFetch=window.fetch.bind(window);
const root=document.getElementById('app');
// Earlier builds asked for the preview password once and kept it in the secure store. The app is now
// recognised by its client marker (see transport.mjs), so any saved password is removed.
const legacyPasswordKey='native_plants_preview';
async function start() {
  if(!Capacitor.isNativePlatform()) throw new Error('Open this build in the Android app. Use the regular website in a browser.');
  window.fetch=previewFetch(nativeFetch,window.API_ORIGIN);
  store.remove({key:legacyPasswordKey}).catch(()=>{});
  root.textContent='Opening the guide…';
  await import('../../../packages/hedge/lib/guest-session.js');
  await window.HedgeGuest.ensureSession();
  await import('../dist/client/Main.js');
  await App.addListener('backButton',() => {
    const close=document.querySelector('.definition-close,.lightbox-close,.account-close');
    if(close) close.click();
    else if(history.length>1) history.back();
    else App.minimizeApp();
  });
  await App.addListener('appStateChange',({isActive}) => {if(isActive) window.dispatchEvent(new Event('focus'));});
  document.addEventListener('click',event => {
    const anchor=event.target.closest?.('a[href]');if(!anchor)return;
    const url=new URL(anchor.href);
    const admin=url.origin===location.origin && url.pathname==='/admin';
    if(admin || url.origin!==location.origin && /^https?:$/.test(url.protocol)) {
      event.preventDefault();event.stopPropagation();
      Browser.open({url:admin ? window.API_ORIGIN+'/admin' : url.href}).catch(()=>{});
    }
  },true);
}
start().catch(error => {
  root.textContent='';const main=document.createElement('main');main.className='preview-unlock';
  const message=document.createElement('p');message.textContent=error.message || 'The guide could not open.';
  const retry=document.createElement('button');retry.textContent='Try again';retry.onclick=()=>location.reload();
  main.append(message,retry);root.append(main);
});
