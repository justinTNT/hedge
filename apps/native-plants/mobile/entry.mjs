import {Capacitor} from '@capacitor/core';
import {App} from '@capacitor/app';
import {Browser} from '@capacitor/browser';
import {SecureStoragePlugin as store} from 'capacitor-secure-storage-plugin';
import {previewFetch} from './transport.mjs';
import './mobile.css';

window.API_ORIGIN=__PLANTS_API_ORIGIN__;
window.HEDGE_MOBILE_RETURN_URL='nativeplants://auth';
const nativeFetch=window.fetch.bind(window);
const key='native_plants_preview';
const root=document.getElementById('app');
async function accepts(credential) {
  const response=await previewFetch(nativeFetch,window.API_ORIGIN,credential)(window.API_ORIGIN+'/api/auth/providers');
  if (response.status===401 || response.status===403) return false;
  if (!response.ok) throw new Error('The preview is temporarily unavailable. Please try again.');
  return true;
}
async function unlock() {
  let saved='';try {saved=(await store.get({key})).value;} catch {}
  if (saved) {
    try {if(await accepts(saved)) return saved;} catch {}
  }
  root.innerHTML=`<main class="preview-unlock"><p class="eyebrow">JOHN BROCK’S FIELD GUIDE</p><h1>Native Plants</h1><p>Enter the private preview password to open the guide.</p><form><label for="preview-password">Preview password</label><input id="preview-password" type="password" autocomplete="current-password" required><button type="submit">Open guide</button><p role="alert"></p></form></main>`;
  return new Promise(resolve => {
    const form=root.querySelector('form'), input=form.querySelector('input'), button=form.querySelector('button'), error=form.querySelector('[role=alert]');
    form.addEventListener('submit',async event => {
      event.preventDefault();if(button.disabled)return;button.disabled=true;error.textContent='';
      try {
        const credential='Basic '+btoa('preview:'+input.value);
        if(!await accepts(credential)) throw new Error('That password did not open the preview. Please try again.');
        await store.set({key,value:credential});input.value='';resolve(credential);
      } catch(e) {error.textContent=e.message || 'Could not connect. Please try again.';button.disabled=false;}
    });
  });
}
async function start() {
  if(!Capacitor.isNativePlatform()) throw new Error('Open this build in the Android app. Use the regular website in a browser.');
  const credential=await unlock();
  window.fetch=previewFetch(nativeFetch,window.API_ORIGIN,credential);
  root.textContent='Opening the guide…';
  await import('../../../packages/hedge/lib/guest-session.js');
  await window.HedgeGuest.ensureSession();
  await import('../dist/client/Main.js');
  await App.addListener('backButton',() => {
    const close=document.querySelector('.lightbox-close,.account-close');
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
