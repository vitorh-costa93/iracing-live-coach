'use strict';
const $=id=>document.getElementById(id);
const Q='https://www.youtube.com/watch?v=UbdPUpVUCDA';
const scenes={
 live:{title:'Volta em andamento',tag:'Observado · 12:52',t:772,description:'SCHUMACHER e seu cronômetro à esquerda; LEHTO e o tempo de referência à direita. A referência permanece quando o relógio ultrapassa esse tempo.',motion:'O cronômetro corre em décimos. A placa permanece fixa; não há necessidade de fazer os tempos deslizar.'},
 split:{title:'Parcial concluído, volta ainda em andamento',tag:'Observado · 13:42',t:822,description:'O cronômetro continua à esquerda; parcial da referência à direita; delta ciano no centro. Abaixo da faixa, o parcial concluído do jogador fica congelado.',motion:'A sequência demonstra placa curta → comparação do parcial → placa curta. A janela de demonstração não é uma medição da transmissão.'},
 result:{title:'Resultado ao completar a volta',tag:'Observado · 13:01 e 14:12',t:781,description:'O tempo final ocupa a esquerda; posição entre parênteses e delta aparecem no centro. O nome e o tempo da referência ficam à direita.',motion:'A posição é um resultado da linha de chegada. Na simulação lenta, o delta permanece ciano; o caso lento é uma proposta de continuidade, não observado neste trecho.'},
 caption:{title:'Legenda do piloto · corrida e classificação',tag:'Observado · Espanha 75:37 / classificação 20:33',t:4537,url:'https://www.youtube.com/watch?v=Ag_ZuKJgbK8',description:'Número amarelo, primeiro nome e sobrenome brancos, equipe em amarelo e espaço para retrato. Confirmada em corrida, inclusive sobre a câmera a bordo.',motion:'O retrato aqui é apenas um símbolo demonstrativo. O SDK não fornece a fotografia; seria uma imagem escolhida pelo usuário.'},
 fastest:{title:'Volta mais rápida da corrida',tag:'Observado · Alemanha 20:33',t:1233,url:'https://www.youtube.com/watch?v=9cMKCIi7fNc',description:'Retrato à esquerda, título ciano, número amarelo, piloto branco, equipe amarela; tempo e velocidade média à direita. O título alemão foi traduzido para FASTEST LAP nesta adaptação.',motion:'A velocidade é a média da volta concluída, não a velocidade instantânea. Ainda não foi medida a duração exata da placa.'},
};
let scene='live',raf=0,start=0,lastPhase='',playing=false;
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
function text(value,x,y,kind='name',anchor='start',color='#f4f4e9',size){
 const factor=Number($(kind==='name'?'nameSize':kind==='delta'?'deltaSize':'timeSize').value)/100;
 return `<text data-kind="${kind}" x="${x}" y="${y}" text-anchor="${anchor}" fill="${color}" font-size="${(size|| (kind==='name'?34:44))*factor}" font-weight="${kind==='name'?$('weight').value:700}">${esc(value)}</text>`;
}
function time(v,digits=1){const m=Math.floor(v/60),s=(v-m*60).toFixed(digits).padStart(m?3+digits:0,'0');return m?`${m}'${s}`:s;}
function background(w){return `<defs><linearGradient id="sky" x2="0" y2="1"><stop stop-color="#a8b2ac"/><stop offset="1" stop-color="#778672"/></linearGradient><filter id="shadow"><feDropShadow dx="3" dy="4" stdDeviation="1" flood-opacity=".95"/></filter></defs><rect width="${w}" height="960" fill="url(#sky)"/><path d="M0 385 Q${w*.4} 280 ${w} 420 L${w} 960 H0Z" fill="#6e8057"/><path d="M${w*.42} 375 C${w*.8} 560 ${w*.08} 620 ${w*.9} 960 H${w*.24} C${w*.08} 650 ${w*.69} 545 ${w*.34} 375Z" fill="#919590" stroke="#deded0" stroke-width="8"/><path d="M0 395 H${w}" stroke="#536459" stroke-width="25"/><path d="M0 376 H${w}" stroke="#c0c8bb" stroke-width="5"/><g transform="translate(${w*.53},540)"><ellipse cx="0" cy="84" rx="100" ry="12" fill="#363b35" opacity=".5"/><path d="M-60 55 L-37 10 L-13 7 L-9 -9 L9 -9 L13 7 L37 10 L60 55Z" fill="#ccaf34"/><path d="M-9 16 H9 V76 H-9Z" fill="#e6ddc1"/><rect x="-78" y="6" width="156" height="9" fill="#d6c977"/><rect x="-68" y="64" width="136" height="11" fill="#cf3a31"/><g fill="#252d2b"><rect x="-76" y="20" width="25" height="42" rx="6"/><rect x="51" y="20" width="25" height="42" rx="6"/><ellipse cy="12" rx="12" ry="15"/></g></g>`;}
function render(elapsed){
 if(elapsed===undefined)elapsed=scene==='split'?44.9:67.9;
 const w=$('aspect').value==='wide'?1706.667:1280, slow=$('negative').checked;
 $('stage').classList.toggle('wide',w>1280);$('canvas').setAttribute('viewBox',`0 0 ${w} 960`);
 const bw=(w-20)*Number($('boardWidth').value)/100,bx=(w-bw)/2,bh=120*Number($('boardHeight').value)/100,by=712+(120-bh)/2;
 const lx=bx+bw*.09,rx=bx+bw*.635,cx=w/2;
 const band=()=>`<rect data-board="main" x="${bx}" y="${by}" width="${bw}" height="${bh}" fill="#555b56" fill-opacity=".48"/>`;
 let g='',yellow='#ffe05b',cyan='#46d5ef';
 if(scene==='caption'){
  const photo=$('portrait').checked;
  const captionHeight=110*Number($('boardHeight').value)/100;
  g=`<rect data-board="main" x="${w*.19}" y="${707+(110-captionHeight)/2}" width="${bw*.55}" height="${captionHeight}" fill="#555b56" fill-opacity=".48"/>`;
  if(photo)g+=`<g transform="translate(${w*.06},678)"><rect width="148" height="183" fill="#99b9ba"/><circle cx="74" cy="55" r="31" fill="#dfd5bb"/><path d="M16 183 V138 Q74 80 132 138 V183Z" fill="#cd4c32"/></g>`;
  g+=text('6',w*.2,751,'time','start',yellow,34)+text('Ayrton SENNA',w*.235,751)+text('McLAREN FORD',w*.2,796,'name','start',yellow,34);
 }else if(scene==='fastest'){
  const h=182*Number($('boardHeight').value)/100,y=650+(182-h)/2;
  g=`<rect data-board="main" x="${bx+bw*.2}" y="${y}" width="${bw*.79}" height="${h}" fill="#555b56" fill-opacity=".4"/>`;
  if($('portrait').checked)g+=`<g transform="translate(${bx+bw*.06},650)"><rect width="154" height="182" fill="#99b9ba"/><circle cx="77" cy="53" r="31" fill="#dfd5bb"/><path d="M16 182 V133 Q77 80 138 133 V182Z" fill="#355071"/></g>`;
  const tx=bx+bw*.2;
  g+=text('FASTEST LAP',tx,695,'name','start',cyan,36)+text("1'45.079",bx+bw*.96,695,'time','end','#f4f4e9',44)+text('2',tx,759,'time','start',yellow,32)+text('Alain PROST',tx+38,759)+text('WILLIAMS RENAULT',tx,815,'name','start',yellow,32)+text('233.48',bx+bw*.87,759,'time','end','#f4f4e9',36)+text('Kmh',bx+bw*.96,759,'name','end',cyan,32)+text('145.08',bx+bw*.87,815,'time','end','#f4f4e9',36)+text('Mph',bx+bw*.96,815,'name','end',cyan,32);
 }else{
  g=band()+text('SCHUMACHER',lx,751);
  if(scene==='live')g+=text(time(slow?elapsed+5.9:elapsed),lx+65,810,'time','start',yellow)+text('LEHTO',rx,751)+text("1'12.830",rx,810,'time','start',yellow);
  if(scene==='split')g+=text(time(elapsed),lx+65,810,'time','start',yellow)+text('SCHUMACHER',rx,751)+text('42.261',rx,810,'time','start',yellow)+text(slow?'+0.290':'−0.290',cx,810,'delta','middle',cyan)+text(slow?'42.551':'41.971',cx,886,'time','middle',yellow);
  if(scene==='result')g+=text(slow?"1'13.127":"1'12.533",lx+35,810,'time','start',yellow)+text(slow?'(4)':'(1)',cx,750,'delta','middle',cyan,32)+text(slow?'+0.297':'−0.297',cx,810,'delta','middle',cyan)+text('LEHTO',rx,751)+text("1'12.830",rx,810,'time','start',yellow);
 }
 $('canvas').innerHTML=background(w)+`<g font-family="Arial, sans-serif" filter="url(#shadow)">${g}</g>`;
 const s=scenes[scene];$('sceneTitle').textContent=s.title;$('description').textContent=s.description;$('evidence').textContent=s.tag;$('evidence').classList.toggle('proposal',!s.t);$('motion').textContent=s.motion;
 $('source').href=s.t?`${s.url||Q}&t=${s.t}s`:'https://www.youtube.com/watch?v=P1PoF7BwWAY';$('source').textContent=s.t?'Abrir trecho de referência ↗':'Abrir referência de corrida ↗';
 document.querySelectorAll('[data-scene]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.scene===scene)));
 ['nameSize','timeSize','deltaSize','boardWidth','boardHeight'].forEach(id=>$(id+'Value').value=$(id).value+'%');
}
function stop(){playing=false;cancelAnimationFrame(raf);$('play').textContent='Reproduzir sequência de qualy';$('phase').textContent='Prévia estática';}
function tick(now){
 const seconds=(now-start)/1000;
 if(seconds>=16){stop();scene='result';render();$('phase').textContent='Sequência concluída';return;}
 const next=seconds<4?'live':seconds<7?'split':seconds<12?'live':'result';
 scene=next;render(seconds<4?37.9+seconds:seconds<7?44.9+seconds-4:67.9+seconds-7);
 if(next!==lastPhase){$('phase').textContent=`${scenes[next].title} · duração demonstrativa`;lastPhase=next;}
 raf=requestAnimationFrame(tick);
}
document.querySelectorAll('[data-scene]').forEach(b=>b.addEventListener('click',()=>{stop();scene=b.dataset.scene;render();}));
document.querySelectorAll('aside input,aside select').forEach(c=>c.addEventListener('input',()=>{if(!playing)render();}));
$('play').addEventListener('click',()=>{if(playing){stop();return;}playing=true;start=performance.now();lastPhase='';$('play').textContent='Parar sequência';raf=requestAnimationFrame(tick);});
$('reset').addEventListener('click',()=>{stop();['nameSize','timeSize','deltaSize','boardWidth','boardHeight'].forEach(id=>$(id).value='100');$('weight').value='700';$('aspect').value='wide';$('portrait').checked=true;$('negative').checked=false;render();});
document.addEventListener('visibilitychange',()=>{if(document.hidden&&playing)stop();});
render();

