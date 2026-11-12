import {NodeIO} from '@gltf-transform/core';import {ALL_EXTENSIONS} from '@gltf-transform/extensions';
const io=new NodeIO().registerExtensions(ALL_EXTENSIONS);
const hex=h=>[1,3,5].map(i=>Math.pow(parseInt(h.slice(i,i+2),16)/255,2.2)).concat([1]);
let d=await io.read('rosie_o.glb');
for(const m of d.getRoot().listMaterials()){const c=m.getBaseColorFactor();const n=m.getName();
 if(n==='whitemediumglossplastic'||(n===''&&c[0]===1)){m.setBaseColorFactor(hex('#2b3236'));m.setMetallicFactor(0);m.setRoughnessFactor(0.55)}
 else if((n==='defaultplastic'||n==='PlasticPolystyrene')&&c[0]>0.4&&c[0]<0.6&&Math.abs(c[0]-c[1])<0.02){m.setBaseColorFactor(hex('#3b4347'));m.setRoughnessFactor(0.6)}
 else if(c[0]===1&&c[1]<0.3){m.setBaseColorFactor(hex('#3fc1b0'));m.setMetallicFactor(0);m.setRoughnessFactor(0.45)}}
await io.write('../rosie.glb',d);
d=await io.read('adam_o.glb');
for(const m of d.getRoot().listMaterials()){m.setBaseColorFactor(hex('#c9d3da'));m.setMetallicFactor(0.5);m.setRoughnessFactor(0.3)}
await io.write('../adam.glb',d);
