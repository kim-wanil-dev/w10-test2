const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const patterns = JSON.parse(fs.readFileSync(path.join(root, 'Assets/Resources/SpellData/patterns.json'), 'utf8').replace(/^\uFEFF/, '')).patterns;
const pieces = JSON.parse(fs.readFileSync(path.join(root, 'Assets/Resources/SpellData/pieces.json'), 'utf8').replace(/^\uFEFF/, '')).pieces;
const esc = s => String(s).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
let out = [];
const W=2400,H=3300;
const rect=(x,y,w,h,fill,r=0,stroke='none')=>out.push(`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${fill}" stroke="${stroke}"/>`);
const text=(x,y,s,size=22,fill='#E8ECF3',weight=400)=>out.push(`<text x="${x}" y="${y}" font-family="Noto Sans KR" font-size="${size}" font-weight="${weight}" fill="${fill}">${esc(s)}</text>`);
const lines=(x,y,s,width=34,size=22,fill='#B7C2D5')=>{let rows=[];for(const line of s.split('\n')){let row='';for(const c of line){row+=c;if(row.length>=width){rows.push(row);row='';}}if(row)rows.push(row);}rows.forEach((v,i)=>text(x,y+i*(size+12),v,size,fill));};
const badge=(x,y,label,on=true)=>{rect(x,y,Math.max(135,label.length*16+30),34,on?'#193E38':'#3D3037',17);text(x+14,y+24,label,17,on?'#97E0C7':'#E4B2BA',600);};
function visual(cells,x,y,w,h,fill,seal=null,clay=false,fixedCellSize=null){
 const triangles=[],edges=new Map();let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
 for(const c of cells){let q=[[c.x,c.y],[c.x+1,c.y],[c.x+1,c.y+1],[c.x,c.y+1]],center=[c.x+.5,c.y+.5];for(let s=0;s<4;s++){if(!(c.mask&(1<<s)))continue;const tri=[q[s],q[(s+1)%4],center];triangles.push(tri);for(const p of tri){minX=Math.min(minX,p[0]);minY=Math.min(minY,p[1]);maxX=Math.max(maxX,p[0]);maxY=Math.max(maxY,p[1]);}for(let i=0;i<3;i++){let a=tri[i],b=tri[(i+1)%3],k=[a.join(','),b.join(',')].sort().join('|');edges.set(k,{a,b,n:(edges.get(k)?.n||0)+1});}}}
 const cellMinX=Math.min(...cells.map(c=>c.x)),cellMinY=Math.min(...cells.map(c=>c.y)),cellMaxX=Math.max(...cells.map(c=>c.x+1)),cellMaxY=Math.max(...cells.map(c=>c.y+1));
 const scale=fixedCellSize||Math.min(w/(maxX-minX),h/(maxY-minY));
 const ox=x+(w-(cellMaxX-cellMinX)*scale)/2-cellMinX*scale,oy=y+(h-(cellMaxY-cellMinY)*scale)/2-cellMinY*scale;
 const pt=p=>`${ox+p[0]*scale},${oy+p[1]*scale}`;
 if(fixedCellSize){for(let gx=cellMinX;gx<cellMaxX;gx++)for(let gy=cellMinY;gy<cellMaxY;gy++){const a=pt([gx,gy]),b=pt([gx+1,gy]),c=pt([gx+1,gy+1]),d=pt([gx,gy+1]);out.push(`<path d="M ${a} L ${b} L ${c} L ${d} Z M ${a} L ${c} M ${b} L ${d}" stroke="#77859B" stroke-opacity=".72" fill="none" stroke-width="1.5"/>`);}}
 if(clay){out.push(`<circle cx="${ox+(cellMinX+cellMaxX)*scale/2}" cy="${oy+(cellMinY+cellMaxY)*scale/2}" r="${scale*.37}" fill="#B88F70" stroke="#D6B492" stroke-width="3"/>`);return;}
 for(const tri of triangles)out.push(`<polygon points="${tri.map(pt).join(' ')}" fill="${fill}"/>`);
 for(const e of edges.values())if(e.n===1)out.push(`<path d="M ${pt(e.a)} L ${pt(e.b)}" stroke="${fill}" fill="none" stroke-width="2"/>`);
 if(seal){let q=[[0,0],[1,0],[1,1],[0,1]],s=seal.sector||0,p=[(q[s][0]+q[(s+1)%4][0]+.5)/3,(q[s][1]+q[(s+1)%4][1]+.5)/3];out.push(`<circle cx="${ox+p[0]*scale}" cy="${oy+p[1]*scale}" r="${.13*scale}" fill="${seal.color}" stroke="#171C27" stroke-width="${.035*scale}"/>`);}
}
const section=(n,title,sub,y)=>{text(64,y,n,20,'#8CA7D4',700);text(118,y,title,36,'#F3F5F9',700);text(118,y+42,sub,21,'#9DAEC7');};
out.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">`);rect(0,0,W,H,'#0D1422');
text(64,75,'SPELL PIECES  /  LIVE GAME CATALOG',20,'#91A8CB',600);text(64,140,'도안 · 조각 · 씰링 · 코팅',55,'#F5F1E8',700);
text(64,188,'현재 코드·JSON 기준  ·  2026.11.12  ·  인게임 이름 / 도형 마스크 / 설명 / 보상 조건',23,'#AEBBD0');
rect(64,220,2272,108,'#182438',18);text(90,258,'보상 읽는 법',23,'#DCE6F7',700);
text(90,296,'조각: 매 전투 3개 중 1개  |  도안: 50% 확률로 등장, 미보유 중 최대 3개에서 1개  |  인장 단독 보상: 현재 비활성',22,'#B8C9DF');
section('01','도안  8종','격자 1칸 = 인게임 격자 1칸 · 모든 미리보기에서 칸 크기 동일',400);
const desc=p=>{let s=p.kind==='Single'?`단일 피해 ${p.baseValue}`:p.kind==='Block'?`방어 ${p.baseValue}`:p.kind==='All'?`전체 피해 ${p.baseValue}`:`조각 1장마다 무작위 피해 ${p.baseValue} (최대 6타)`;return p.keyword==='OneStroke'?s+' · 한 방: 조각 1장으로 완성하면 +6':p.keyword==='Mosaic'?'모자이크 · '+s:p.keyword==='Purity'?s+' · 순수: 한 가지 도형으로만 완성하면 +6':s;};
patterns.forEach((p,i)=>{let x=64+i%4*574,y=475+Math.floor(i/4)*370;rect(x,y,550,345,'#182235',18);text(x+24,y+44,p.name,30,p.color,700);badge(x+290,y+18,p.isStarter?'시작 보유':'보상 후보',!p.isStarter);visual(p.cells,x+90,y+72,370,142,p.color,null,false,32);lines(x+24,y+235,desc(p),27,20,'#E1E7F2');text(x+24,y+325,p.isStarter?'일반 시작: 이미 보유 → 보상 제외':'미보유일 때만 도안 보상에 등장',17,'#93A6C2');});
section('02','기본 조각  11종','위 도안 미리보기와 동일한 칸 크기 · 1칸 = 인게임 격자 1칸',1290);
pieces.forEach((p,i)=>{let x=64+i%6*382,y=1360+Math.floor(i/6)*275;rect(x,y,358,250,'#182235',16);text(x+20,y+35,p.name,24,'#E9EEF6',700);visual(p.cells,x+44,y+58,270,115,'#C4D1D6',null,p.shape==='Clay',32);badge(x+20,y+192,p.isInRewardPool?'보상 O':'보상 X',p.isInRewardPool);if(p.shape==='Clay')text(x+177,y+216,'고유 · 효과 부착 불가',14,'#C3AD99');else if(p.rotation==='MirrorAlternate')text(x+177,y+216,'뒤집기 포함 회전',14,'#98A9C3');});
text(64,1937,'보상에서 제외: 맞춤 조각 · 큰 삼각형 · L자 조각  /  효과 없는 일반형 그대로는 점토만 등장합니다.',21,'#C6B59A');
section('03','씰링  4종','조각의 1/4 구역 위 색 동그라미 + 어두운 테두리 · 같은 씰링은 개수만큼 중복 발동',2030);
const seals=[['회수','#55C7B3','시전 후 회수 씰링을 뺀 복사본 1장을 손패로 가져옴 (이번 전투 동안)'],['파편','#F25973','시전 시 무작위 적에게 2 피해'],['보충','#EDD98C','시전 시 조각 1장 뽑기'],['덧칠','#E673D9','조각 전체를 놓인 조각 위에 한 겹 겹쳐 놓을 수 있음 · 수치 +2']];
seals.forEach(([n,c,d],i)=>{let x=64+i*574,y=2100;rect(x,y,550,365,'#182235',18);text(x+24,y+42,n,29,c,700);visual([{x:0,y:0,mask:15}],x+195,y+65,160,120,'#C4D1D6',{color:c});text(x+24,y+219,'표시색 '+c+' · 구역 위 원형 표식',18,'#9DAEC7');lines(x+24,y+256,d,28,21,'#E1E7F2');badge(x+24,y+315,'보상 O · 조각에 부착');});
text(64,2506,'씰링 단독 「인장 획득」 보상은 현재 OFF. 랜덤 조각 보상의 씰링 4종은 모두 ON.',21,'#C6B59A');
section('04','코팅  3종','조각 전체의 색과 Material로 표현 · 조각당 하나 · 고유 점토는 코팅 불가',2595);
const coatings=[['수호','#4F95D6','시전 시 방어 +3','3D: 매끄러운 표면 · Metallic 0.05 / Smoothness 0.92'],['증폭','#E07A3F','피해 +2 · 다타 주문은 타격마다 +1','3D: 광택 표면 · Metallic 0.35 / Smoothness 0.75'],['톱니','#B88C61','변을 맞댄 조각 1장당 피해 +1 (최대 +3)','3D: 금속 표면 · Metallic 0.95 / Smoothness 0.42']];
coatings.forEach(([n,c,d,m],i)=>{let x=64+i*766,y=2665;rect(x,y,742,340,'#182235',18);text(x+24,y+43,n,30,c,700);visual([{x:0,y:0,mask:15}],x+25,y+80,140,140,c);text(x+197,y+106,'2D: '+c+' 전체 채움',20,'#A7B7CE');lines(x+197,y+147,d,25,21,'#E1E7F2');badge(x+197,y+228,'보상 O · 조각에 부착');text(x+24,y+307,m,17,'#A0AEC3');});
text(64,3070,'랜덤 조각: 점토가 아니면 씰링 1개 또는 코팅 1개를 50:50으로 부착. 점토는 후보 3개 중 최대 1개.',22,'#C7D1E1');
text(64,3120,'현재 보상 제외 효과  ·  마침표: 도안을 완성시킨 마지막 조각이면 수치 +3  /  공명: 3장 이상 함께 소모되면 수치 +3',20,'#9EABC0');
text(64,3185,'SOURCE  patterns.json · pieces.json · SpellGame.PatternDescription · SpellGameUI 씰링/코팅 설명 · EnterReward / RandomRewardPiece',17,'#72859F');
text(64,3230,'도형은 현재 cells/mask를 직접 도식화. 점토만 인게임의 둥근 고유 외형으로 표시. 3D 재질은 2D 색 견본과 수치로 병기.',18,'#72859F');
out.push('</svg>');fs.writeFileSync(path.join(__dirname,'game-catalog.svg'),out.join('\n'),'utf8');
console.log(JSON.stringify({file:path.join(__dirname,'game-catalog.svg'),patterns:patterns.length,pieces:pieces.length,shapesInReward:pieces.filter(p=>p.isInRewardPool).length}));
