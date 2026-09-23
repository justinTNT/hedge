import { cpSync, mkdirSync } from 'node:fs';
mkdirSync('public', {recursive:true});
cpSync('../../packages/hedge/src/Admin/admin.css','public/admin.css');
