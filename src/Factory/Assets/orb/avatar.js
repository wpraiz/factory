// ODIN · FACE — Odin's avatar on the Factory orb, generated from the approved prototype (visual direction "Silent
// Forge"): lean face, tall spiky punk hair. Do NOT edit orb-gl.js/orb.js (shared orb modules): this file draws its
// own orb + face on a layer above, and factory-orb.js only passes the state.
// API: criarAvatar(host) -> { set({modo,nivel_audio,mao,sintonia,seed}), parar() }  (avatar-state/v1)
// modos: idle | ouvindo | pensando | falando | mao | fiscal_ok | fiscal_falha
export function criarAvatar(HOST, OPTS={}) {   // ES modules are already strict; 'use strict' here plus a default parameter is a SyntaxError (it once took the orb down)
const CV=document.createElement('canvas'), HUD=document.createElement('canvas');
for (const c of [CV, HUD]) { c.style.cssText='position:absolute;inset:0;width:100%;height:100%;display:block;pointer-events:none'; HOST.appendChild(c); }
const VIVO={on:true};
function renderRegs(){} function syncUI(){} function updateState(){}
const REG = ["olho-cibernetico","olho-carne","testa","sobrancelhas","nariz","bochecha-e","bochecha-d","boca","barba-alta","barba-baixa","cabelo-e","cabelo-d"];
const RI = Object.fromEntries(REG.map((r,i)=>[r,i]));

/* --- same mechanism as the reference avatar contract (avatar-state/v1) --- */
function rng(seed){let x=seed>>>0||1;return()=>((x^=x<<13,x^=x>>>17,x^=x<<5)>>>0)/4294967296}
// Rodada 2 (revisores): o sorteio de peso igual deixava o cabelo aparecer antes do rosto. Agora a ordem é curada
// por faixas e a seed só embaralha DENTRO de cada faixa — each seed still gets its own path, mas o rosto nasce
// de dentro para fora: olho -> testa/sobrancelhas -> nariz/olho de carne -> boca/bochechas -> barba -> cabelo.
// Mesmo xorshift e mesmo limiar (i+0.5)/12 do contrato; only the order changes.
const FAIXAS=[["olho-cibernetico"],["testa","sobrancelhas"],["nariz","olho-carne"],["boca","bochecha-e","bochecha-d"],["barba-alta","barba-baixa"],["cabelo-e","cabelo-d"]];
function limiares(seed){const r=rng(seed),o=[];for(const f of FAIXAS){const a=f.slice();for(let i=a.length-1;i>0;i--){const j=Math.floor(r()*(i+1));[a[i],a[j]]=[a[j],a[i]]}o.push(...a)}const t={};o.forEach((id,i)=>t[id]=(i+0.5)/o.length);return t}

/* ------------------------------ geometria do rosto ------------------------------ */
// Unidades de rosto: x para a direita do espectador, y para BAIXO, z para fora da tela.
const TAU = Math.PI*2;
const g  = (v,m,s)=>Math.exp(-0.5*((v-m)/s)**2);
const g2 = (x,y,mx,my,sx,sy)=>g(x,mx,sx)*g(y,my,sy);
const sm = (a,b,v)=>{const t=Math.min(1,Math.max(0,(v-a)/(b-a)));return t*t*(3-2*t)};
const lerp=(a,b,t)=>a+(b-a)*t;
const R0 = rng(7331); const rand = ()=>R0();
const EC=[-0.33,-0.205];   // olho cibernético (o que foi de Mímir) — esquerda do espectador
const EF=[ 0.33,-0.205];   // olho de carne

// Leaner face: widths ~15% smaller from skull to chin
const HW=[[-1.70,0.27],[-1.58,0.48],[-1.40,0.59],[-1.15,0.66],[-0.90,0.69],[-0.55,0.71],[-0.10,0.715],[0.30,0.665],[0.70,0.58]];
function halfW(y){
  if(y<=HW[0][0]) return HW[0][1];
  for(let i=1;i<HW.length;i++) if(y<=HW[i][0]){const t=(y-HW[i-1][0])/(HW[i][0]-HW[i-1][0]);return lerp(HW[i-1][1],HW[i][1],t*t*(3-2*t))}
  return HW[HW.length-1][1];
}
// Relevo do rosto: um elipsoide esculpido por gaussianas (arco da sobrancelha, órbitas, nariz, maçãs).
function H(x,y){
  const w=halfW(y), ex=Math.min(0.999,Math.abs(x)/(w*1.04));
  let z=0.56*Math.sqrt(1-ex*ex);
  z*= 1-0.22*sm(-0.95,-1.65,y);
  z+= 0.105*g(y,-0.445,0.065)*g(x,0,0.52);
  z+= 0.05*(g2(x,y,-0.30,-0.41,0.14,0.05)+g2(x,y,0.30,-0.41,0.14,0.05));
  z-= 0.15*(g2(x,y,EC[0],-0.21,0.14,0.10)+g2(x,y,EF[0],-0.21,0.14,0.10));
  const t=sm(-0.42,0.26,y);
  z+= (0.04+0.27*t*t)*g(x,0,0.065+0.05*t)*(1-sm(0.27,0.40,y));
  z+= 0.07*g2(x,y,0,0.225,0.075,0.07);
  z+= 0.05*(g2(x,y,-0.112,0.262,0.055,0.05)+g2(x,y,0.112,0.262,0.055,0.05));
  z+= 0.085*(g2(x,y,-0.44,0.0,0.12,0.11)+g2(x,y,0.44,0.0,0.12,0.11));
  z-= 0.06*(g2(x,y,-0.40,0.30,0.10,0.10)+g2(x,y,0.40,0.30,0.10,0.10));
  z-= 0.05*(g2(x,y,-0.63,-0.62,0.08,0.18)+g2(x,y,0.63,-0.62,0.08,0.18));
  z+= 0.025*g2(x,y,0,-0.53,0.05,0.05);
  return z;
}
const LN=(()=>{const v=[0.42,-0.58,0.70];const l=Math.hypot(...v);return v.map(c=>c/l)})();
function skinShade(x,y){
  const e=0.008, dx=(H(x+e,y)-H(x-e,y))/(2*e), dy=(H(x,y+e)-H(x,y-e))/(2*e);
  const l=Math.hypot(dx,dy,1), n=[-dx/l,-dy/l,1/l];
  const dif=Math.max(0,n[0]*LN[0]+n[1]*LN[1]+n[2]*LN[2]);
  const rim=Math.pow(1-n[2],1.5);
  return 0.12+0.8*dif*dif+0.9*rim;
}
function regionSkin(x,y){
  if(Math.hypot(x-EC[0],y-EC[1])<0.21) return RI["olho-cibernetico"];
  if(Math.hypot(x-EF[0],y-EF[1])<0.21) return RI["olho-carne"];
  if(y<-0.50) return RI["testa"];
  if(Math.abs(x)<0.165 && y>-0.46) return RI["nariz"];
  return x<0?RI["bochecha-e"]:RI["bochecha-d"];
}
const beardTop = x=>0.68-0.93*(x/0.68)**2;

const LV=[];   // linhas: x,y,z, região, tipo, aux, sombra  (7 floats por vértice)
const PV=[];   // partículas: x,y,z, região, rnd, tipo, tamanho
// tipos de linha: 0 varredura · 2 fio de barba · 3 cabelo · 4 traço de feição · 5 anel do olho (aux = giro)
//                 6 ruga da testa (aux = posição 0..1) · 7 lábio inferior (abre com a voz) · 8 circuito do olho
function poly(pts,o){
  // tipo + id da linha empacotados (inteiro = tipo, fração = id): cada fio se dissolve com a sua própria fase,
  // senão centenas de fios caem na mesma curva do ruído e somam luz em riscos falsos
  const reg=o.reg, kind=o.kind+0.98*rand();
  for(let i=0;i<pts.length;i++){
    const p=pts[i];
    if(p.r===undefined) p.r = o.regFn?o.regFn(p[0],p[1]):reg;
    if(p.a===undefined) p.a = o.auxFn?o.auxFn(p,i):(o.aux||0);
    if(p.s===undefined) p.s = o.shFn?o.shFn(p,i):skinShade(p[0],p[1])+(o.shAdd||0);
    if(o.dust!==0 && rand()<(o.dust||0.3)){
      const j=o.jit||0.004;
      PV.push(p[0]+(rand()-0.5)*j,p[1]+(rand()-0.5)*j,p[2]+(rand()-0.5)*j,p.r,rand(),0,rand());
    }
  }
  for(let i=0;i<pts.length-1;i++){
    const a=pts[i],b=pts[i+1];
    LV.push(a[0],a[1],a[2],a.r,kind,a.a,a.s, b[0],b[1],b[2],b.r,kind,b.a,b.s);
  }
}
function bez(p0,p1,p2,p3,t){const u=1-t;return [u*u*u*p0[0]+3*u*u*t*p1[0]+3*u*t*t*p2[0]+t*t*t*p3[0], u*u*u*p0[1]+3*u*u*t*p1[1]+3*u*t*t*p2[1]+t*t*t*p3[1]]}
const onSkin=(x,y,dz=0.004)=>[x,y,H(x,y)+dz];

function buildFace(){
  /* 1) varredura: curvas horizontais que seguem o relevo (é o "scan") */
  for(let y=-1.60;y<=0.41;y+=0.031){
    const w=halfW(y)*0.99; let cur=[];
    const flush=()=>{if(cur.length>1) poly(cur,{kind:0,regFn:regionSkin,dust:0.22}); cur=[]};
    for(let x=-w;x<=w+1e-9;x+=0.0145){
      const dC=Math.hypot(x-EC[0],(y-EC[1])*1.08);
      const inEye=((x-EF[0])/0.14)**2+((y-EF[1]+0.004)/0.05)**2<1;
      const beard=(y>0.31&&Math.abs(x)<0.30)||y>beardTop(x)+0.01;
      if(dC<0.148||inEye||beard){flush();continue}
      cur.push(onSkin(x,y,0.002));
    }
    flush();
  }

  /* 1b) meridianos esparsos: a malha vertical que fecha o "wireframe" sem virar grade */
  for(let x=-0.77;x<=0.771;x+=0.077){
    let cur=[];
    const flush=()=>{if(cur.length>1) poly(cur,{kind:1,regFn:regionSkin,dust:0}); cur=[]};
    for(let y=-1.62;y<=0.42;y+=0.012){
      if(Math.abs(x)>halfW(y)*0.99||(Math.abs(x)<0.17&&y>-0.47&&y<0.34)){flush();continue}
      const dC=Math.hypot(x-EC[0],(y-EC[1])*1.08);
      const inEye=((x-EF[0])/0.14)**2+((y-EF[1]+0.004)/0.05)**2<1;
      const beard=(y>0.31&&Math.abs(x)<0.30)||y>beardTop(x)+0.01;
      if(dC<0.148||inEye||beard){flush();continue}
      cur.push(onSkin(x,y,0.002));
    }
    flush();
  }

  /* 2) testa marcada: rugas longas, quebradas, e o "11" entre as sobrancelhas */
  for(let k=0;k<6;k++){
    const y0=-0.60-k*0.098-(rand()*0.01), w=halfW(y0)*(0.80-k*0.03);
    const gaps=[lerp(-w,w,0.2+rand()*0.2),lerp(-w,w,0.55+rand()*0.3)];
    let cur=[];
    const flush=()=>{if(cur.length>1) poly(cur,{kind:6,reg:RI["testa"],shAdd:0.25,auxFn:p=>(p[0]+w)/(2*w),dust:0.35}); cur=[]};
    for(let x=-w;x<=w;x+=0.011){
      if(gaps.some(gx=>Math.abs(x-gx)<0.028+0.02*(k%2))){flush();continue}
      const y=y0-0.032*(1-(x/w)**2)*(k<4?1:0.3)+0.006*Math.sin(x*15+k*2.3)+0.004*Math.sin(x*37+k);
      cur.push(onSkin(x,y,0.007));
    }
    flush();
  }
  for(const s of [-1,1]){
    const pts=[];for(let t=0;t<=1.0001;t+=0.1){const y=lerp(-0.585,-0.415,t);pts.push(onSkin(s*(0.052-0.012*t+0.004*Math.sin(t*6)),y,0.008))}
    poly(pts,{kind:4,reg:RI["testa"],shAdd:0.2});
    const p2=[];for(let t=0;t<=1.0001;t+=0.2){const y=lerp(-0.56,-0.46,t);p2.push(onSkin(s*(0.085-0.01*t),y,0.006))}
    poly(p2,{kind:4,reg:RI["testa"],shAdd:0.05});
  }

  /* 3) sobrancelhas: tufos grossos, fios soltos, a do lado de Mímir cortada pela cicatriz */
  for(const s of [-1,1]){
    const N=115;
    for(let i=0;i<N;i++){
      // solene: canto interno baixo e cerrado, arco firme, ponta externa que NÃO cai (antes lia triste)
      const t=Math.pow(rand(),0.9), bx=s*(0.07+0.48*t);
      if(s<0 && Math.abs(bx+0.40)<0.028) continue;
      const by=-0.365-0.052*Math.sin(Math.PI*Math.min(1,t*1.05))-0.012*t+(rand()-0.5)*0.032;
      let ang=lerp(-0.6,-0.08,t)+(rand()-0.5)*0.4;
      const len=(0.045+rand()*0.05)*(t>0.75&&rand()<0.3?1.7:1);
      const pts=[]; let x=bx,y=by;
      for(let k=0;k<=4;k++){
        pts.push([x,y,H(x,y)+0.035+0.01*k/4]);
        x+=s*Math.cos(ang)*len/4; y+=Math.sin(ang)*len/4; ang+=0.05;
      }
      poly(pts,{kind:4,reg:RI["sobrancelhas"],shAdd:0.22,dust:0.25});
    }
  }

  /* 4) olho de carne: pálpebra pesada, íris sob a pálpebra, pés de galinha, bolsas */
  {
    // olhar firme: pálpebra superior reta por cima da íris (sem caimento externo), abertura um pouco maior
    const up=x=>{const u=(x-0.2)/0.27;return EF[1]+0.010-0.046*Math.sin(Math.PI*Math.pow(u,0.9))-0.004*u};
    const lo=x=>{const u=(x-0.2)/0.27;return EF[1]+0.012+0.028*Math.sin(Math.PI*Math.pow(u,1.1))};
    const reg=RI["olho-carne"];
    const A=[],B=[],F=[],F2=[];
    for(let x=0.2;x<=0.4701;x+=0.01){A.push([x,up(x),H(x,up(x))+0.04]);B.push([x,lo(x),H(x,lo(x))+0.035])}
    poly(A,{kind:4,reg,shAdd:0.85});poly(B,{kind:4,reg,shAdd:0.4});
    const A2=[];for(let x=0.21;x<=0.4601;x+=0.01)A2.push([x,up(x)-0.004,H(x,up(x))+0.041]);
    poly(A2,{kind:4,reg,shAdd:0.55});
    for(let x=0.20;x<=0.49;x+=0.01){const u=(x-0.20)/0.29;const y=EF[1]-0.05-0.03*Math.sin(Math.PI*Math.pow(u,0.8))-0.004*u;F.push([x,y,H(x,y)+0.03])}
    poly(F,{kind:4,reg,shAdd:0.45});
    for(let x=0.26;x<=0.50;x+=0.012){const u=(x-0.26)/0.24;const y=EF[1]-0.032-0.012*Math.sin(Math.PI*u)+0.008*u*u;F2.push([x,y,H(x,y)+0.025])}
    poly(F2,{kind:4,reg,shAdd:0.2});
    // pálpebra encapuzada: dobras paralelas por cima, sem caimento (velho e atento, não cansado)
    for(let k=0;k<2;k++){const P=[];for(let x=0.215+k*0.01;x<=0.47;x+=0.01){const u=(x-0.215)/0.255;const y=EF[1]-0.024-0.036*Math.sin(Math.PI*Math.pow(u,0.85))-k*0.01;P.push([x,y,H(x,y)+0.035])}poly(P,{kind:4,reg,shAdd:0.25-k*0.12,dust:0.1})}
    const LL=[];for(let x=0.205;x<=0.4651;x+=0.01){const y=lo(x)+0.009;LL.push([x,y,H(x,y)+0.03])}
    poly(LL,{kind:4,reg,shAdd:0.05});
    // cílios curtos na pálpebra superior
    for(let x=0.23;x<=0.45;x+=0.016){const y=up(x),a=-1.2+(x-0.23)*2.2;poly([[x,y,H(x,y)+0.042],[x+Math.cos(a)*0.012,y+Math.sin(a)*0.014,H(x,y)+0.043]],{kind:4,reg,shAdd:0.4,dust:0})}
    const ic=[0.336,EF[1]+0.002], ir=0.038, I=[];let I2=[];
    for(let a=0;a<=TAU+0.01;a+=TAU/72){
      const x=ic[0]+Math.cos(a)*ir, y=ic[1]+Math.sin(a)*ir;
      if(y>up(x)+0.003 && y<lo(x)-0.002){I2.push([x,y,H(x,y)+0.045])} else {if(I2.length>1)I.push(I2);I2=[]}
    }
    if(I2.length>1)I.push(I2);
    I.forEach(p=>poly(p,{kind:4,reg,shAdd:0.65}));
    // fibras da íris: raios curtos entre pupila e borda (o olho humano ganha matéria, não brilho)
    for(let k=0;k<22;k++){const a=k/22*TAU+0.1;const r0=0.018,r1=0.034;const p0=[ic[0]+Math.cos(a)*r0,ic[1]+Math.sin(a)*r0],p1=[ic[0]+Math.cos(a)*r1,ic[1]+Math.sin(a)*r1];
      if(p0[1]<up(p0[0])+0.004||p1[1]<up(p1[0])+0.004||p1[1]>lo(p1[0])-0.003)continue;
      poly([[p0[0],p0[1],H(ic[0],ic[1])+0.046],[p1[0],p1[1],H(ic[0],ic[1])+0.046]],{kind:4,reg,shAdd:0.45,dust:0})}
    const ir2=0.017;let I3=[];
    for(let a=0;a<=TAU+0.01;a+=TAU/32){const x=ic[0]+Math.cos(a)*ir2,y=ic[1]+Math.sin(a)*ir2;if(y>up(x)+0.003)I3.push([x,y,H(x,y)+0.046]);else{if(I3.length>1)poly(I3,{kind:4,reg,shAdd:0.7});I3=[]}}
    if(I3.length>1)poly(I3,{kind:4,reg,shAdd:0.7});
    PV.push(ic[0]+0.009,ic[1]-0.006,H(ic[0],ic[1])+0.05,reg,0.5,3,1);
    for(let k=0;k<6;k++){
      const a=-0.6+k*0.25+(rand()-0.5)*0.1, L=0.07+rand()*0.06, P=[];
      for(let t=0;t<=1.001;t+=0.2){const x=0.48+Math.cos(a)*L*t, y=EF[1]+0.002+Math.sin(a)*L*t;P.push([x,y,H(x,y)+0.006])}
      poly(P,{kind:4,reg,shAdd:0.3});
    }
    for(let k=0;k<3;k++){const P=[];for(let x=0.22+k*0.01;x<=0.47-k*0.015;x+=0.012){const u=(x-0.22)/0.25;const y=EF[1]+0.055+k*0.032+0.022*Math.sin(Math.PI*u);P.push([x,y,H(x,y)+0.006])}poly(P,{kind:4,reg:k<2?reg:RI["bochecha-d"],shAdd:0.0})}
  }

  /* 5) olho cibernético: anéis concêntricos, marcas de circuito, cicatriz atravessando */
  {
    const reg=RI["olho-cibernetico"], zE=H(EC[0],EC[1])+0.075;
    const at=(r,a)=>[EC[0]+Math.cos(a)*r, EC[1]+Math.sin(a)*r, zE];
    const arc=(r,a0,a1,spin,n=64)=>{const P=[];for(let k=0;k<=n;k++)P.push(at(r,lerp(a0,a1,k/n)));poly(P,{kind:5,reg,aux:spin,shFn:()=>1,dust:0.15})};
    arc(0.024,0,TAU,1.5,48);
    for(let k=0;k<10;k++){const a0=k/10*TAU;arc(0.039,a0,a0+0.62*TAU/10,-0.9,8)}
    arc(0.052,0,TAU,0,72);
    for(let k=0;k<3;k++){const a0=k/3*TAU+0.3;arc(0.066,a0,a0+0.78*TAU/3,0.55,30)}
    for(let k=0;k<72;k++){const a=k/72*TAU,l=k%6===0?0.017:0.007;poly([at(0.078,a),at(0.078+l,a)],{kind:5,reg,aux:-0.25,shFn:()=>1,dust:0})}
    for(let k=0;k<2;k++){const a0=k*Math.PI+0.4;arc(0.104,a0,a0+0.9,0.3,24);poly([at(0.098,a0),at(0.110,a0)],{kind:5,reg,aux:0.3,shFn:()=>1,dust:0});poly([at(0.098,a0+0.9),at(0.110,a0+0.9)],{kind:5,reg,aux:0.3,shFn:()=>1,dust:0})}
    arc(0.122,3.6,5.9,0,40);
    // órbita: aro irregular + trilhas de circuito para a têmpora e a maçã
    const rim=[];for(let k=0;k<=90;k++){const a=k/90*TAU,r=0.142+0.006*Math.sin(a*5)+0.004*Math.sin(a*11);const x=EC[0]+Math.cos(a)*r,y=EC[1]+Math.sin(a)*r*0.96;rim.push([x,y,H(x,y)+0.01])}
    poly(rim,{kind:8,reg,shFn:()=>0.4,dust:0.2});
    const traces=[[2.9,-0.63,0.0],[3.25,-0.66,0.03],[3.55,-0.60,0.05],[2.35,-0.58,0.0]];
    for(const [a,xe,dy] of traces){
      const p0=[EC[0]+Math.cos(a)*0.145,EC[1]+Math.sin(a)*0.14], p1=[EC[0]+Math.cos(a)*0.19,EC[1]+Math.sin(a)*0.185+dy];
      const p2=[xe,p1[1]];const P=[p0,p1,p2].map(q=>[q[0],q[1],H(q[0],q[1])+0.01]);
      poly(P,{kind:8,reg,shFn:()=>0.4,dust:0});
      const s=0.008,z=H(xe,p1[1])+0.01;
      poly([[xe-s,p1[1]-s,z],[xe+s,p1[1]-s,z],[xe+s,p1[1]+s,z],[xe-s,p1[1]+s,z],[xe-s,p1[1]-s,z]],{kind:8,reg,shFn:()=>0.5,dust:0});
    }
    for(const [a,len] of [[1.2,0.1],[1.55,0.14]]){
      const p0=[EC[0]+Math.cos(a)*0.145,EC[1]+Math.sin(a)*0.14],p1=[p0[0]-0.02,p0[1]+len*0.5],p2=[p1[0],p0[1]+len];
      poly([p0,p1,p2].map(q=>[q[0],q[1],H(q[0],q[1])+0.01]),{kind:8,reg,shFn:()=>0.4,dust:0});
    }
    // cicatriz: dois cortes paralelos da testa à maçã, interrompidos pelo implante, com pontos
    for(const off of [0,0.011]){
      let cur=[];
      for(let t=0;t<=1.0001;t+=0.012){
        const x=lerp(-0.425,-0.262,t)+0.006*Math.sin(t*23)+off, y=lerp(-0.66,0.17,t);
        if(Math.hypot(x-EC[0],y-EC[1])<0.15){if(cur.length>1)poly(cur,{kind:4,reg,shAdd:0.55-off*20});cur=[];continue}
        cur.push(onSkin(x,y,0.012));
      }
      if(cur.length>1)poly(cur,{kind:4,reg,shAdd:0.55-off*20});
    }
    for(const t of [0.06,0.14,0.22,0.72,0.8,0.88]){
      const x=lerp(-0.425,-0.262,t)+0.0055, y=lerp(-0.66,0.17,t);
      poly([onSkin(x-0.018,y-0.004,0.012),onSkin(x+0.018,y+0.004,0.012)],{kind:4,reg,shAdd:0.2,dust:0});
    }
    PV.push(EC[0],EC[1],zE+0.01,reg,0.5,1,1);   // halo do olho
    PV.push(EC[0],EC[1],zE+0.02,reg,0.5,2,1);   // núcleo
  }

  /* 6) nariz: dorso, asas, ponta, narinas; sulcos nasogenianos nas bochechas */
  {
    // Rodada 2: nada de dois trilhos paralelos. O dorso é UM traço longo do lado da luz, nascendo no canto interno
    // da sobrancelha; o lado da sombra é só um fragmento + poeira. Asas e narinas são arcos curtos de comprimentos
    // diferentes, afinando nas pontas (sombra baixa nas extremidades = traço some em vez de terminar seco).
    const reg=RI["nariz"];
    const taper=(p,k,n,add)=>skinShade(p[0],p[1])+add*Math.pow(Math.sin(Math.PI*Math.min(1,Math.max(0,k/n))),0.6)-0.25*(1-Math.sin(Math.PI*k/n));
    {const P=[],n=26;for(let k=0;k<=n;k++){const b=bez([0.10,-0.445],[0.03,-0.33],[0.038,0.0],[0.078,0.185],k/n);P.push(onSkin(b[0],b[1],0.006))}
     poly(P,{kind:4,reg,shFn:(p,k)=>taper(p,k,n,0.55)});}
    {const P=[],n=16;for(let k=0;k<=n;k++){const t=lerp(0.4,0.95,k/n);const b=bez([-0.10,-0.44],[-0.035,-0.32],[-0.05,-0.02],[-0.083,0.16],t);P.push(onSkin(b[0],b[1],0.005))}
     poly(P,{kind:4,reg,shFn:(p,k)=>taper(p,k,n,0.05)});}
    for(let i=0;i<260;i++){const t=rand(),b=bez([-0.10,-0.44],[-0.035,-0.32],[-0.05,-0.02],[-0.083,0.16],0.3+0.7*t);const x=b[0]-rand()*0.05*(0.4+t),y=b[1]+(rand()-0.5)*0.02;
      PV.push(x,y,H(x,y)+0.006,reg,rand(),0,rand()*0.5)}
    for(const s of [-1,1]){
      const arcs=s>0?[[0.050,-1.1,0.9,0.45],[0.044,-0.3,0.7,0.15]]:[[0.050,-0.5,0.8,0.05]];
      for(const [r,a0,a1,add] of arcs){const P=[],n=18;for(let k=0;k<=n;k++){const a=lerp(a0,a1,k/n);P.push(onSkin(s*0.103+s*Math.cos(a)*r,0.258+Math.sin(a)*r*0.9,0.006))}
        poly(P,{kind:4,reg,shFn:(p,k)=>taper(p,k,n,add+0.2)})}
      const Nn=[],n=10;for(let k=0;k<=n;k++){const b=bez([s*0.03,0.306],[s*0.05,0.31],[s*0.066,0.304],[s*0.072,0.294],k/n);Nn.push(onSkin(b[0],b[1],0.004))}
      poly(Nn,{kind:4,reg,shFn:(p,k)=>taper(p,k,n,s>0?0.2:0.0)});
      const F=[];for(let t=0;t<=1.0001;t+=0.05){const b=bez([s*0.165,0.20],[s*0.24,0.28],[s*0.30,0.40],[s*0.37,0.56],t);F.push(onSkin(b[0],b[1],0.008))}
      poly(F,{kind:4,reg:s<0?RI["bochecha-e"]:RI["bochecha-d"],shAdd:0.2});
      const C=[];for(let t=0;t<=1.0001;t+=0.05){const b=bez([s*0.36,-0.08],[s*0.46,-0.02],[s*0.58,0.02],[s*0.68,0.12],t);C.push(onSkin(b[0],b[1],0.006))}
      poly(C,{kind:4,reg:s<0?RI["bochecha-e"]:RI["bochecha-d"],shAdd:0.05});
    }
    {const T=[],n=12;for(let k=0;k<=n;k++){const x=lerp(-0.035,0.045,k/n);T.push(onSkin(x,0.318+0.006*Math.cos(x/0.045*Math.PI/2),0.01))}
     poly(T,{kind:4,reg,shFn:(p,k)=>taper(p,k,n,0.3)});}
    {const T2=[],n=12;for(let k=0;k<=n;k++){const a=lerp(Math.PI*1.2,Math.PI*1.85,k/n);T2.push(onSkin(0.006+Math.cos(a)*0.048,0.24+Math.sin(a)*0.034,0.012))}
     poly(T2,{kind:4,reg,shFn:(p,k)=>taper(p,k,n,0.6)});}
  }

  /* 7) boca (quase escondida pelo bigode) */
  {
    const reg=RI["boca"];
    PV.push(0,0.64,H(0,0.64)+0.06,reg,0.5,4,1);   // halo da voz (âmbar), só acende falando
    const U=[];for(let x=-0.14;x<=0.1401;x+=0.01)U.push(onSkin(x,0.618+0.008*(x/0.14)**2,0.03));
    poly(U,{kind:4,reg,shAdd:0.3});
    const L=[];for(let x=-0.16;x<=0.1601;x+=0.008)L.push(onSkin(x,0.636-0.004*Math.cos(x*9)+0.012*(x/0.16)**4,0.03));
    poly(L,{kind:7,reg,shAdd:0.45,dust:0.4});
    const L2=[];for(let x=-0.085;x<=0.0851;x+=0.01)L2.push(onSkin(x,0.668+0.004*(1-(x/0.085)**2),0.03));
    poly(L2,{kind:7,reg,shAdd:0.0});
    for(let k=1;k<3;k++){const L3=[],yy=0.636+k*0.01,w=0.12-k*0.025;for(let x=-w;x<=w+1e-4;x+=0.01)L3.push(onSkin(x,yy+0.002*(1-(x/w)**2),0.028));poly(L3,{kind:7,reg,shAdd:-0.15,dust:0.2})}
  }

  /* 8) bigode: fios caindo pelos cantos da boca */
  {
    for(let i=0;i<190;i++){
      const u=rand()*2-1, s=u<0?-1:1, au=Math.abs(u);
      const x0=u*0.27, y0=0.335+0.035*au*au+(rand()-0.5)*0.015;
      const k1=Math.pow(au,1.3);
      const p3=[lerp(x0*1.25,s*(0.46+0.1*rand()),k1), lerp(0.645,0.95+0.25*rand(),k1)];
      const p1=[x0+s*0.04*au, y0+0.08], p2=[lerp(x0,p3[0],0.7)+s*0.04*au, lerp(y0,p3[1],0.55)];
      const ph=rand()*TAU, P=[];
      for(let k=0;k<=14;k++){const t=k/14;const b=bez([x0,y0],p1,p2,p3,t);b[0]+=0.006*Math.sin(t*9+ph);P.push([b[0],b[1],0.60-0.28*Math.abs(b[0])+0.035*Math.sin(Math.PI*t)])}
      const lum=0.55+0.45*rand();
      poly(P,{kind:2,reg:RI["barba-alta"],shFn:(p,k)=>(0.3+0.35*Math.min(1,Math.abs(p[0])/0.2)+0.2*s-0.15*(k/14))*lum,dust:0.3,jit:0.012});
    }
  }

  /* 9) barba: ~340 fios longos que partem da mandíbula e afunilam até a ponta */
  {
    const NB=340;
    for(let i=0;i<NB;i++){
      const u=(i/(NB-1))*2-1+(rand()-0.5)*0.014, au=Math.min(1,Math.abs(u)), s=Math.sign(u)||1;
      const x0=u*0.68, y0=beardTop(x0)+(rand()-0.5)*0.035+(au<0.25?0.012:0);
      // mechas: fios do mesmo tufo compartilham fase e convergem de leve — lê como cabelo, não como cone
      const clump=Math.floor((u+1)*6.5), cph=clump*2.39, cx=((clump+0.5)/6.5-1);
      const len=0.82+0.18*rand();
      const p3=[lerp(u,cx,0.35)*0.30+(rand()-0.5)*0.05, lerp(y0,2.08-0.42*u*u,len)];
      const p1=[x0*1.2+s*0.04*au, y0+0.45*(1-0.3*au)], p2=[lerp(x0,p3[0],0.5)*1.18, lerp(y0,p3[1],0.62)];
      const ph=cph+rand()*0.6, P=[], z0=0.55-0.38*au*au, z1=0.22*(1-au)+0.04, sway=(clump*0.137)%1;
      const NS=28;
      for(let k=0;k<=NS;k++){
        const t=k/NS, b=bez([x0,y0],p1,p2,p3,t);
        b[0]+=0.02*Math.sin(t*7+ph)*t;
        const z=lerp(z0,z1,t)+0.13*Math.sin(Math.PI*t)*(1-au*0.6);
        const p=[b[0],b[1],z]; p.r=b[1]<1.15+(Math.sin(x0*37.1)*0.09+Math.sin(k*0.7+x0*11.0)*0.025)?RI["barba-alta"]:RI["barba-baixa"]; p.a=sway;
        P.push(p);
      }
      poly(P,{kind:2,shFn:(p,k)=>{const t=k/NS;return 0.3+0.55*(1-au*au)+0.2*u-0.3*t+0.1*Math.pow(au,4)+0.12*Math.sin(cph*3.1)},dust:0.34,jit:0.02});
    }
    // queixo: fios curtos e densos logo abaixo do lábio, fechando o vão entre bigode e barba
    for(let i=0;i<90;i++){
      const u=rand()*2-1, x0=u*0.24, y0=0.69+0.02*rand()+0.05*u*u;
      const p3=[u*0.3+(rand()-0.5)*0.06, 1.25+0.35*rand()], ph=rand()*TAU, P=[];
      for(let k=0;k<=14;k++){const t=k/14;const b=bez([x0,y0],[x0*1.05,y0+0.15],[lerp(x0,p3[0],0.6),lerp(y0,p3[1],0.6)],p3,t);b[0]+=0.008*Math.sin(t*9+ph);
        const p=[b[0],b[1],0.62-0.2*u*u+0.06*Math.sin(Math.PI*t)];p.r=b[1]<1.15+(Math.sin(x0*37.1)*0.09+Math.sin(k*0.7+x0*11.0)*0.025)?RI["barba-alta"]:RI["barba-baixa"];P.push(p)}
      poly(P,{kind:2,shFn:(p,k)=>0.55+0.35*(1-Math.abs(u))+0.15*u+0.3*Math.max(0,1-k/3),dust:0.3,jit:0.012});
    }
    // duas contas de ferro prendendo a ponta (detalhe nórdico)
    for(const [y,w] of [[1.70,0.105],[1.78,0.095]]){
      const P=[];for(let a=0;a<=TAU+0.01;a+=TAU/48){const x=Math.cos(a)*w,z=0.2+Math.sin(a)*0.05;if(z>0.17)P.push([x,y+Math.sin(a)*0.012,z]);else if(P.length){poly(P.splice(0),{kind:4,reg:RI["barba-baixa"],shFn:()=>0.8})}}
      if(P.length>1)poly(P,{kind:4,reg:RI["barba-baixa"],shFn:()=>0.8});
    }
  }

  /* 10) PUNK hair (big, tall, spiky, rough). Espetos em leque
     saindo da cupula do cranio; cada espeto = feixe de fios que converge na ponta, com serrilha (tosco) e ponta acesa. */
  {
    const SK=0.72, E=(phi,k=1)=>[Math.sin(phi)*SK*k, -1.0-Math.cos(phi)*0.74*k];
    // Rodada 2 (primeiro print parecia coroa): menos espetos, grossos e DESIGUAIS, tortos, com serrilha forte; 2 camadas.
    const espeto=(phi,len,bw,bend,lum,zb,nS)=>{
      const base=E(phi,0.99), reg=Math.sin(phi)<0?RI["cabelo-e"]:RI["cabelo-d"];
      const d=[Math.sin(phi)*0.85+(rand()-0.5)*0.45, -Math.cos(phi)-0.40+(rand()-0.5)*0.2], dl=Math.hypot(d[0],d[1]);
      const tip=[base[0]+d[0]/dl*len, base[1]+d[1]/dl*len];
      const nx=-d[1]/dl, ny=d[0]/dl;   // normal ao espeto: pra onde ele entorta
      const tg=[Math.cos(phi)*SK, Math.sin(phi)*0.74], tl=Math.hypot(tg[0],tg[1]);
      for(let i=0;i<nS;i++){
        const v=(i/(nS-1))*2-1, b0=[base[0]+tg[0]/tl*v*bw, base[1]+tg[1]/tl*v*bw];
        const ph=rand()*TAU, NS=18, P=[];
        for(let k=0;k<=NS;k++){
          const t=k/NS, curva=bend*Math.sin(Math.PI*t)*len, serr=(rand()-0.5)*0.06*(1-t*0.6);
          const x=lerp(b0[0],tip[0],t)+nx*(curva+serr)+0.012*Math.sin(t*13+ph)*(1-t);
          const y=lerp(b0[1],tip[1],t)+ny*(curva+serr);
          P.push([x,y,zb*(1-0.5*t)+0.03*Math.sin(Math.PI*t)]);
        }
        poly(P,{kind:3,reg,shFn:(p,k)=>(0.28+0.22*(1-k/NS)+0.55*Math.pow(k/NS,4))*lum,dust:0.35,jit:0.03});
      }
    };
    // Rodada 3 (revisores: "morno, nem coroa nem punk"): 11 espetos GROSSOS, os do meio muito mais altos, bem tortos
    const NSP=11;
    for(let j=0;j<NSP;j++){
      const u=(j+0.5)/NSP*2-1, c=1-Math.abs(u);
      const phi=u*1.30+(rand()-0.5)*0.30;
      const len=(0.45+1.55*Math.pow(c,1.4))*(0.65+0.7*rand());   // centro chega a ~2x os lados; nenhum igual
      espeto(phi,len,0.14+0.12*rand(),(rand()-0.5)*0.34,0.75+0.25*rand(),0.30*Math.cos(phi)+0.09,24+Math.floor(rand()*14));
    }
    // camada de trás: espetos curtos e escuros entre os grandes (volume, sem virar coroa)
    for(let j=0;j<12;j++){
      const phi=(rand()*2-1)*1.2;
      espeto(phi,0.25+0.45*rand(),0.06+0.05*rand(),(rand()-0.5)*0.3,0.35+0.25*rand(),0.04*Math.cos(phi),8+Math.floor(rand()*6));
    }
    // laterais curtas (raspado) + linha de implantacao na testa
    for(const s of [-1,1]){
      const reg=s<0?RI["cabelo-e"]:RI["cabelo-d"];
      for(let i=0;i<70;i++){
        const phi=s*(1.05+0.45*rand()), p0=E(phi,1.0), P=[];
        for(let k=0;k<=3;k++){const t=k/3;P.push([p0[0]+s*0.03*t,p0[1]-0.05*t+0.02*rand(),0.12*Math.cos(phi)+0.05])}
        poly(P,{kind:3,reg,shAdd:0.05,dust:0.25});
      }
      for(let i=0;i<45;i++){
        const x=s*(0.03+0.48*rand()), y=-1.40-0.06*(1-Math.abs(x)/0.5)+(rand()-0.5)*0.03;
        const P=[];for(let k=0;k<=4;k++){const t=k/4;const xx=x+s*0.03*t, yy=y-0.16*t;P.push([xx,yy,H(xx,yy)+0.012])}
        poly(P,{kind:3,reg,shAdd:0.15,dust:0.3});
      }
    }
  }

  /* poeira de volume na barba e no cabelo (a "matéria" da orb) */
  for(let i=0;i<1400;i++){
    const u=rand()*2-1, t=rand(), au=Math.abs(u);
    const y=lerp(beardTop(u*0.68),2.05-0.45*u*u,t), x=u*lerp(0.73,0.2,t*t)+(rand()-0.5)*0.05;
    PV.push(x,y,0.3+0.2*(1-au)*(1-t),y<1.15?RI["barba-alta"]:RI["barba-baixa"],rand(),0,rand());
  }
}
buildFace();

/* ------------------------------------ WebGL ------------------------------------ */
const cv=CV, hud=HUD, hx=hud.getContext('2d');
const gl=cv.getContext('webgl',{alpha:false,antialias:true,powerPreference:'high-performance'});
if(!gl) throw new Error('sem webgl');

const COMMON=`
precision highp float;
uniform vec2 u_center; uniform vec2 u_res; uniform float u_scale; uniform float u_orbR; uniform float u_dpr;
uniform float u_yaw; uniform float u_pitch; uniform float u_time;
uniform float u_sharp[12]; uniform float u_glow[12];
uniform vec2 u_eye; uniform float u_eyeLit; uniform float u_ring;
uniform float u_mouth; uniform float u_talk; uniform float u_think; uniform float u_breath;
uniform vec3 u_pulseCol; uniform float u_pulse;
uniform float u_val; uniform float u_ativ; uniform float u_caos; uniform float u_fogo; uniform float u_blink; uniform float u_roll; uniform float u_surp;
uniform vec3 u_hairA; uniform vec3 u_hairB;   // cor do cabelo: raiz -> ponta (mao gesto, cor_cabelo)
// vida(): cada regiao do rosto e uma funcao do estado (ver docs\\avatar-expressoes.md)
vec3 vida(vec3 p, int r){
  if(p.y<-0.55) p.y=-0.55+(p.y+0.55)*0.78;                       // testa 22% menor (cabelo desce junto)
  float smile=max(u_val,0.0), frown=max(-u_val,0.0);
  if(r==3){ float ins=1.0-clamp(abs(p.x)/0.6,0.0,1.0);             // sobrancelhas: surpresa sobe; raiva/preocupacao aperta o meio
    p.y+= -0.045*u_surp -0.015*u_ativ + 0.05*frown*ins - 0.02*smile*(1.0-ins) + 0.006*sin(u_time*0.7); }
  if(r==1){ float d=length(p.xy-vec2(0.33,-0.205));                 // olho de carne: piscada fecha na linha do olho
    float k=1.0-smoothstep(0.07,0.2,d); p.y=mix(p.y,-0.205,u_blink*k*0.92); }
  if(r==7){ float c=clamp(abs(p.x)/0.17,0.0,1.0);                   // boca: cantos sobem (valencia+) ou caem (valencia-)
    p.y+= (-0.05*smile + 0.045*frown)*c*c; }
  if(r==5||r==6){ vec2 bc=vec2(sign(p.x)*0.36,0.05);                // bochechas sobem no sorriso
    p.y-=0.035*smile*(1.0-smoothstep(0.05,0.35,length(p.xy-bc))); }
  if(r==2){ p.y+=0.012*(frown+u_think+0.3*u_caos)*sin(p.x*18.0+u_time*1.5); }   // testa: rugas vivas
  if(r==8||r==9){ float d=max(p.y-0.35,0.0);                         // barba: respira sempre, balanca com a fala, agita com caos
    p.x+= d*(0.02*sin(u_time*0.9+p.y*1.8) + 0.03*u_talk*sin(u_time*3.1+p.y*2.4) + 0.018*u_caos*sin(u_time*5.0+p.y*6.0+p.x*9.0));
    p.y+= d*(0.01*sin(u_time*0.7+p.x*3.0) - 0.05*u_fogo*(0.5+0.5*sin(u_time*7.0+p.x*20.0))*d); }
  if(r==10||r==11){ float t=clamp((-1.15-p.y)/1.3,0.0,1.0);          // cabelo: vivo sempre; caos arrepia e tremula; fogo sobe e lambe
    p.x+= t*(0.04*sin(u_time*1.1+p.y*2.2+p.x*3.0)*(0.4+u_ativ) + 0.025*u_caos*sin(u_time*17.0+p.x*41.0+p.y*23.0) + 0.05*u_fogo*sin(u_time*9.0+p.y*7.0+p.x*13.0));
    p.y+= -t*(0.20*u_caos + 0.22*u_fogo*(0.6+0.4*sin(u_time*6.0+p.x*25.0))); }
  float c=cos(u_roll), s=sin(u_roll); p.xy=vec2(p.x*c-p.y*s, p.x*s+p.y*c);   // cabeca inclina
  return p;
}
const vec3 VIOLET=vec3(0.50,0.30,1.00); const vec3 BLUE=vec3(0.27,0.50,1.00); const vec3 ICE=vec3(0.80,0.91,1.00);
const vec3 CYAN=vec3(0.00,0.90,1.00); const vec3 AMBER=vec3(1.00,0.69,0.00);
vec3 rotp(vec3 p){
  float ca=cos(u_pitch), sa=sin(u_pitch);
  p=vec3(p.x, p.y*ca-p.z*sa, p.y*sa+p.z*ca);
  float cb=cos(u_yaw), sb=sin(u_yaw);
  return vec3(p.x*cb+p.z*sb, p.y, -p.x*sb+p.z*cb);
}
vec2 toScreen(vec3 p, out float persp){
  vec3 q=rotp(p); persp=6.0/(6.0-q.z);
  return u_center+q.xy*u_scale*persp;
}
vec4 clipOf(vec2 px){ vec4 c=vec4(px/u_res*2.0-1.0,0.0,1.0); c.y=-c.y; return c; }
`;

const LINE_VS=COMMON+`
attribute vec3 a_pos; attribute vec4 a_meta;   // região, tipo, aux, sombra
varying vec3 v_col;
bool is(float k, float v){ return abs(k-v)<0.5; }
void main(){
  int r=int(a_meta.x+0.5); float s=u_sharp[r]; float gw=u_glow[r];
  float kind=floor(a_meta.y); float lid=fract(a_meta.y)*40.0; vec3 p=a_pos;
  bool ring=is(kind,5.0), cyb=ring||is(kind,8.0);
  if(ring){ vec2 d=p.xy-u_eye; float an=u_ring*a_meta.z; float c=cos(an), si=sin(an); p.xy=u_eye+vec2(d.x*c-d.y*si, d.x*si+d.y*c); }
  if(is(kind,7.0)){ float k=clamp(1.0-pow(p.x/0.17,2.0),0.0,1.0); p.y+=u_mouth*0.06*k; }
  if(is(kind,2.0)){ float d=max(p.y-0.45,0.0); p.x+=sin(p.y*2.6-u_time*(0.7+u_talk*2.2)+a_meta.z*6.28)*0.009*d*(u_talk*2.5); }
  p=vida(p,r);
  float un=1.0-s;
  vec3 n=vec3(sin(p.y*11.0+u_time*0.9+p.x*4.0+lid), sin(p.x*9.0-u_time*0.8+p.y*3.0+lid*1.7), sin((p.x-p.y)*7.0+u_time*0.6+lid*0.6));
  p+=n*un*un*0.09;
  float persp; vec2 px=toScreen(p,persp);
  gl_Position=clipOf(px);
  float dist=length(px-u_center)/u_orbR;
  float fade=is(kind,3.0) ? 1.0-smoothstep(1.05,1.5,dist) : 1.0-smoothstep(0.62,1.0,dist);   // cabelo punk passa da orb
  float sh=a_meta.w;
  vec3 col=mix(VIOLET,BLUE,smoothstep(0.15,0.65,sh));
  col=mix(col,ICE,smoothstep(0.75,1.35,sh));
  if(is(kind,3.0)) col=mix(u_hairA,u_hairB,smoothstep(0.55,1.1,sh));
  if(is(kind,3.0)&&u_fogo>0.01){ float h=clamp((-1.2-a_pos.y)/1.3,0.0,1.0); float fl=0.5+0.5*sin(u_time*9.0+a_pos.x*30.0+a_pos.y*12.0);
    col=mix(col, mix(vec3(1.0,0.22,0.03),vec3(1.0,0.75,0.15),fl*h), u_fogo*(0.35+0.65*h)); }   // cabelo pegando fogo
  float I=0.27;
  if(is(kind,1.0)) I=0.13; else if(is(kind,2.0)) I=0.19; else if(is(kind,3.0)) I=0.30; else if(is(kind,4.0)) I=0.55; else if(is(kind,6.0)) I=0.45; else if(is(kind,7.0)) I=0.55;
  I*=0.30+0.85*sh;
  if(is(kind,3.0)) I*=1.0+1.6*u_fogo+0.5*u_caos;
  if(r==4 && kind<1.5) I*=0.7;   // varredura recua sobre o nariz: quem desenha o nariz é o contorno, não os degraus
  if(is(kind,6.0)){ float ph=fract(u_time*0.30); float b=exp(-pow((a_meta.z-ph)*6.0,2.0)); I+=u_think*b*0.9; col=mix(col,ICE,u_think*b); }
  if(r==2 && kind<0.5){ float b=exp(-pow((fract(p.x*0.5+0.5-u_time*0.22)-0.5)*9.0,2.0)); I+=u_think*b*0.25; }
  // voz: a luz âmbar nasce na boca e se espalha pelo bigode/barba com queda suave (sem fronteira de região)
  vec2 md=(a_pos.xy-vec2(0.0,0.66))*vec2(1.1,0.75);
  float vw=exp(-dot(md,md)*3.2)*u_talk;
  col=mix(col,AMBER,clamp(vw*(0.35+u_mouth*1.1),0.0,0.92));
  I*=1.0+gw*0.4+vw*u_mouth*1.8;
  if(cyb){
    col=mix(mix(CYAN,VIOLET*1.3,u_think*0.65),u_pulseCol,u_pulse);
    I=(ring?0.95:0.38)*(0.40+0.95*u_eyeLit)*(1.0+u_pulse*0.8);
  } else {
    col=mix(col,u_pulseCol,u_pulse*0.16); I*=1.0+u_pulse*0.25;
  }
  I*=mix((kind>1.5&&kind<3.5)?0.045:0.09,1.0,s*s);
  I*=fade*(0.86+0.14*u_breath)*(1.0+0.5*u_talk);
  v_col=col*I;
}`;
const LINE_FS=`precision highp float; varying vec3 v_col; void main(){ gl_FragColor=vec4(v_col,1.0); }`;

const PT_VS=COMMON+`
attribute vec3 a_pos; attribute vec4 a_meta;   // região, rnd, tipo, tamanho
varying vec3 v_col; varying float v_kind;
void main(){
  int r=int(a_meta.x+0.5); float s=u_sharp[r]; float un=1.0-s;
  float rnd=a_meta.y, kind=a_meta.z; vec3 p=a_pos;
  float ph=rnd*6.2831853;
  if(kind<0.5){
    vec3 dir=normalize(vec3(cos(ph*3.1), sin(ph*5.7), 0.6*cos(ph*1.3))+vec3(1e-4));
    float rr=fract(rnd*7.13); float rad=0.015+0.10*rr+0.18*rr*rr*rr;
    vec3 drift=vec3(sin(u_time*0.23+ph), cos(u_time*0.19+ph*1.3), 0.0)*0.035;
    p+=(dir*rad+drift)*un;
  }
  p=vida(p,r);
  float persp; vec2 px=toScreen(p,persp);
  gl_Position=clipOf(px);
  float fade=1.0-smoothstep(0.66,1.02,length(px-u_center)/u_orbR);
  vec3 col; float I; float sz;
  if(kind<0.5){
    float tw=0.55+0.45*sin(u_time*(0.8+rnd*2.2)+ph*4.0);
    col=mix(VIOLET,BLUE,fract(rnd*3.7)); col=mix(col,ICE,0.3*s);
    vec2 md=(a_pos.xy-vec2(0.0,0.66))*vec2(1.1,0.75);
    float vw=exp(-dot(md,md)*3.2)*u_talk;
    I=mix(0.10,0.30,un)*tw*fade*(1.0+vw*u_mouth*1.2);
    col=mix(col,AMBER,clamp(vw*(0.25+u_mouth*0.9),0.0,0.7));
    sz=(1.0+0.9*un+a_meta.w*0.7)*persp;
  } else if(kind<1.5){
    col=mix(CYAN,u_pulseCol,u_pulse); I=(0.12+0.40*u_eyeLit)*mix(0.35,1.0,s)*(1.0+u_pulse*1.2);
    sz=u_scale*(0.75+0.55*u_eyeLit)*persp;
  } else if(kind<2.5){
    col=mix(mix(CYAN,u_pulseCol,u_pulse),vec3(1.0),0.55); I=(0.55+0.9*u_eyeLit)*mix(0.3,1.0,s);
    sz=(7.0+5.0*u_eyeLit)*persp;
  } else if(kind<3.5){
    col=ICE; I=0.1+0.65*s; sz=5.0*persp;
  } else {
    // brasa da voz: halo âmbar na boca, só existe falando e segue o nível de saída
    col=AMBER; I=(0.06+0.55*u_mouth)*u_talk*mix(0.4,1.0,s); sz=u_scale*(0.55+0.6*u_mouth)*persp;
  }
  gl_PointSize=max(1.0,sz*u_dpr);
  v_col=col*I; v_kind=kind;
}`;
const PT_FS=`precision highp float; varying vec3 v_col; varying float v_kind;
void main(){ vec2 d=gl_PointCoord-0.5; float r=length(d)*2.0; float a;
  if((v_kind>0.5&&v_kind<1.5)||v_kind>3.5) a=exp(-r*r*4.5)*(1.0-smoothstep(0.8,1.0,r)); else a=1.0-smoothstep(0.3,1.0,r);
  if(a<0.003) discard; gl_FragColor=vec4(v_col*a,1.0); }`;

/* orb: mesmo shader de Assets/orb/orb-gl.js; única mudança: o miolo recua (u_hole) para o rosto sair dele,
   e a borda ganha o "rádio do universo" (u_radio) quando o pensar passa de 8 s */
const RINGS=190, MAXP=900;
const ORB_VS=`
precision highp float;
attribute float a_ring; attribute float a_index; attribute float a_count;
uniform float u_time; uniform float u_level; uniform float u_bass; uniform float u_treble; uniform float u_base;
uniform vec2 u_center; uniform vec2 u_res; uniform float u_dpr; uniform float u_radio;
varying float v_f; varying float v_bright;
const float TAU=6.28318530718;
void main(){
  float f=a_ring/${(RINGS-1).toFixed(1)};
  float theta=(a_index/a_count)*TAU;
  float layer=a_ring*0.013;
  float w=1.00*sin(3.0*theta+u_time*0.62+layer*3.0)+0.55*sin(5.0*theta-u_time*0.94+layer*5.0)
         +0.32*sin(7.0*theta+u_time*1.31+layer*7.0)+0.18*sin(11.0*theta-u_time*1.77+layer*11.0);
  float bandGain=1.0+u_bass*0.8*(1.0-f)+u_treble*1.1*f;
  float amp=(0.09+u_level*0.38)*bandGain;
  float radius=u_base*(0.002+f*0.998)*(1.0+u_level*0.16);
  float z=w*amp*radius*0.40*(0.30+f);
  float rr=radius+w*amp*radius*0.10;
  rr+=u_radio*pow(f,6.0)*sin(a_index*12.9898+a_ring*78.233+u_time*37.0)*u_base*0.05;
  vec3 p=vec3(cos(theta)*rr, sin(theta)*rr, z);
  float ax=0.30+sin(u_time*0.21)*0.09; float ay=sin(u_time*0.13)*0.22;
  p=vec3(p.x, p.y*cos(ax)-p.z*sin(ax), p.y*sin(ax)+p.z*cos(ax));
  p=vec3(p.x*cos(ay)+p.z*sin(ay), p.y, -p.x*sin(ay)+p.z*cos(ay));
  float focal=u_base*3.4; float persp=focal/max(focal-p.z, focal*0.55);
  vec2 px=u_center+p.xy*persp;
  gl_Position=vec4((px/u_res)*2.0-1.0,0.0,1.0); gl_Position.y=-gl_Position.y;
  gl_PointSize=max(1.0,(2.35-f*0.85)*persp*u_dpr);
  v_f=f; v_bright=persp*(0.55+u_level*0.75);
}`;
const ORB_FS=`
precision highp float;
varying float v_f; varying float v_bright;
uniform float u_level; uniform float u_hole; uniform float u_gain;
void main(){
  vec2 d=gl_PointCoord-0.5; float r=length(d); float a=smoothstep(0.5,0.12,r);
  if(a<=0.001) discard;
  vec3 roxo=vec3(0.62,0.36,1.00), azul=vec3(0.30,0.55,1.00), ciano=vec3(0.22,0.88,1.00);
  vec3 cor=v_f<0.62?mix(roxo,azul,v_f/0.62):mix(azul,ciano,(v_f-0.62)/0.38);
  cor=mix(cor,vec3(1.0),(1.0-v_f)*u_level*0.28);
  float intensidade=a*v_bright*(0.14+v_f*0.34)*(0.85+u_level*1.05);
  intensidade*=mix(u_hole,1.0,smoothstep(0.35,0.92,v_f))*u_gain;
  gl_FragColor=vec4(cor*intensidade,1.0);
}`;
const BG_VS=`attribute vec2 a_pos; varying vec2 v_uv; void main(){ v_uv=a_pos*0.5+0.5; gl_Position=vec4(a_pos,0.0,1.0); }`;
const BG_FS=`precision highp float; varying vec2 v_uv; uniform vec2 u_res; uniform vec2 u_c; uniform float u_level; uniform float u_breath;
void main(){
  vec2 px=vec2(v_uv.x, 1.0-v_uv.y)*u_res; float r=length(px-u_c)/(u_res.y*0.55);
  vec3 bg=vec3(0.039,0.055,0.078);
  vec3 ember=vec3(0.231,0.118,0.478);
  float e=exp(-r*r*2.2)*(0.42+0.08*u_breath+0.25*u_level);
  gl_FragColor=vec4(bg+ember*e*0.55,1.0);
}`;

function mk(vs,fs){
  const c=(t,s)=>{const sh=gl.createShader(t);gl.shaderSource(sh,s);gl.compileShader(sh);if(!gl.getShaderParameter(sh,gl.COMPILE_STATUS))throw new Error(gl.getShaderInfoLog(sh));return sh};
  const p=gl.createProgram();gl.attachShader(p,c(gl.VERTEX_SHADER,vs));gl.attachShader(p,c(gl.FRAGMENT_SHADER,fs));gl.linkProgram(p);
  if(!gl.getProgramParameter(p,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(p));
  p.u={};const n=gl.getProgramParameter(p,gl.ACTIVE_UNIFORMS);
  for(let i=0;i<n;i++){const info=gl.getActiveUniform(p,i);const name=info.name.replace(/\[0\]$/,'');p.u[name]=gl.getUniformLocation(p,name)}
  return p;
}
const P_LINE=mk(LINE_VS,LINE_FS), P_PT=mk(PT_VS,PT_FS), P_ORB=mk(ORB_VS,ORB_FS), P_BG=mk(BG_VS,BG_FS);

function buf(data){const b=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,b);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(data),gl.STATIC_DRAW);return b}
const B_LINE=buf(LV), N_LINE=LV.length/7;
const B_PT=buf(PV), N_PT=PV.length/7;
const B_BG=buf([-1,-1,3,-1,-1,3]);
let N_ORB=0; const B_ORB=(()=>{const d=[];for(let i=0;i<RINGS;i++){const f=i/(RINGS-1);const n=Math.max(48,Math.round(MAXP*(0.18+f*0.82)));for(let p=0;p<n;p++)d.push(i,p,n);N_ORB+=n}return buf(d)})();

function bindAttrs(p,b,layout,stride){
  gl.bindBuffer(gl.ARRAY_BUFFER,b);
  for(let i=0;i<8;i++) gl.disableVertexAttribArray(i);
  for(const [name,size,off] of layout){const l=gl.getAttribLocation(p,name);if(l<0)continue;gl.enableVertexAttribArray(l);gl.vertexAttribPointer(l,size,gl.FLOAT,false,stride*4,off*4)}
}

/* ------------------------------------ estado ------------------------------------ */
const Q=new URLSearchParams(OPTS.teste||'');   // so pra print de teste (instant/hold); no app fica vazio
const st={schema:"avatar-state/v1",t:Date.now(),modo:Q.get('modo')||'idle',nivel_audio:+(Q.get('audio')??0.6),pensando_ms:0,mao:Q.get('mao')||'criar_arquivo',sintonia:+(Q.get('s')??0.30),seed:+(Q.get('seed')??42)};
let thr=limiares(st.seed);
Object.assign(st,{valencia:0.15,ativacao:0.3,caos:0.35,fogo:0});
const E={val:0.15,ativ:0.3,caos:0.35,fogo:0,surp:0,blink:0,roll:0,evVal:0,evAtiv:0,evFogo:0,evSurp:0,blinkT:-9,nextBlink:1.5,
  nod:0,turn:0,beatT:-9,beatSide:1,prevVox:0,falaT:0};
// Requested gesture (voice "shake your head" used to get no response): sim = nod, nao = shake, balancar = sway
// lateral, inclinar = cabeca de lado. Envelope sin(pi*t/dur), soma por cima do movimento vivo.
const G={tipo:null,t0:0,dur:0,amp:1};
const GESTOS={sim:1.3,nao:1.4,balancar:2.6,inclinar:2.2};
// Hair colour: dark root -> light tip of the same colour.
const HAIR_PADRAO={a:[0.24,0.28,0.38],b:[0.80,0.91,1.00]};
const HAIR={a:[...HAIR_PADRAO.a],b:[...HAIR_PADRAO.b]};
const CORES={vermelho:[1,0.12,0.1],fogo:[1,0.42,0.05],laranja:[1,0.55,0.1],amarelo:[1,0.85,0.15],dourado:[1,0.72,0.25],
  verde:[0.2,1,0.35],ciano:[0,0.9,1],azul:[0.25,0.45,1],roxo:[0.58,0.3,1],violeta:[0.58,0.3,1],rosa:[1,0.35,0.75],
  branco:[0.95,0.97,1],prata:[0.75,0.8,0.88],preto:[0.12,0.13,0.17]};
function corCabelo(v){
  const s=String(v||'').trim().toLowerCase();
  if(!s||s==='padrao'||s==='padrão'||s==='normal')return Object.assign(HAIR,{a:[...HAIR_PADRAO.a],b:[...HAIR_PADRAO.b]}),true;
  let c=null; const hx=s.match(/^#?([0-9a-f]{6})$/);
  if(hx)c=[0,2,4].map(i=>parseInt(hx[1].slice(i,i+2),16)/255);
  else{const k=Object.keys(CORES).find(n=>s.startsWith(n.slice(0,4)));if(k)c=CORES[k]}
  if(!c)return false;
  HAIR.a=c.map(x=>x*0.32); HAIR.b=c.map(x=>x+(1-x)*0.35); return true;
}
const sharp=new Float32Array(12), glow=new Float32Array(12);
const instant=Q.has('instant');
let modeStart=performance.now()-(+(Q.get('pms')||0));
let pulseStart=-1e9, pulseCol=[0,0,0];
const sm1=(c,t,r,dt)=>c+(t-c)*Math.min(1,r*dt);
const V={eyeLit:0.35,ring:0,ringSpeed:0.15,mouth:0,talk:0,think:0,radio:0,level:0.05,runesA:0,maoA:0,eyeRunes:0};

function setModo(m){
  st.modo=m; modeStart=performance.now();
  if(m==='fiscal_ok'||m==='fiscal_falha'){pulseStart=performance.now();pulseCol=m==='fiscal_ok'?[0.22,1.0,0.08]:[1.0,0.16,0.43]}
}
// API para o Factory: window.odinAvatar.set({...avatar-state/v1})
window.odinAvatar={set(o){if(o.seed!==undefined&&o.seed!==st.seed){st.seed=o.seed;thr=limiares(st.seed);renderRegs()}
  if(o.sintonia!==undefined)st.sintonia=o.sintonia;
  for(const k of ['valencia','ativacao','caos','fogo']) if(o[k]!==undefined) st[k]=Math.max(k==='valencia'?-1:0,Math.min(1,+o[k]));
  if(o.evento==='ok'){E.evVal+=0.7;E.evSurp+=0.4} if(o.evento==='falha'){E.evVal-=0.8;E.evAtiv+=0.3} if(o.evento==='tarefa'){E.evAtiv+=0.35} if(o.evento==='fogo'){E.evFogo=1}
  if(o.gesto&&GESTOS[o.gesto]){G.tipo=o.gesto;G.t0=performance.now();G.dur=GESTOS[o.gesto];G.amp=o.amp??1}
  if(o.cor_cabelo!==undefined&&!corCabelo(o.cor_cabelo))return false;
  if(o.nivel_audio!==undefined)st.nivel_audio=o.nivel_audio; if(o.mao!==undefined)st.mao=o.mao;
  if(o.modo&&o.modo!==st.modo)setModo(o.modo); if(o.pensando_ms!==undefined)modeStart=performance.now()-o.pensando_ms; syncUI(); return true},
  pose(){return {yaw,pitch,roll:E.roll,gesto:G.tipo}}};   // so leitura: teste de gesto sem print

/* ------------------------------------ runas ------------------------------------ */
// Futhark antigo, traço reto (runas foram feitas para serem entalhadas): cada uma é uma lista de polilinhas 0..1.
const FUTHARK={
 'ᚠ':[[[.3,0],[.3,1]],[[.3,.12],[.8,-.05]],[[.3,.42],[.8,.22]]],'ᚢ':[[[.25,1],[.25,0],[.75,.3],[.75,1]]],
 'ᚦ':[[[.3,0],[.3,1]],[[.3,.25],[.72,.5],[.3,.75]]],'ᚨ':[[[.3,0],[.3,1]],[[.3,.05],[.75,.32]],[[.3,.35],[.75,.62]]],
 'ᚱ':[[[.3,1],[.3,0],[.72,.25],[.3,.5],[.75,1]]],'ᚲ':[[[.72,0],[.3,.5],[.72,1]]],'ᚷ':[[[.2,0],[.8,1]],[[.8,0],[.2,1]]],
 'ᚹ':[[[.3,1],[.3,0],[.72,.25],[.3,.5]]],'ᚺ':[[[.25,0],[.25,1]],[[.75,0],[.75,1]],[[.25,.35],[.75,.6]]],
 'ᚾ':[[[.5,0],[.5,1]],[[.3,.35],[.7,.6]]],'ᛁ':[[[.5,0],[.5,1]]],'ᛃ':[[[.48,.1],[.2,.35],[.48,.6]],[[.52,.4],[.8,.65],[.52,.9]]],
 'ᛇ':[[[.5,0],[.5,1]],[[.5,0],[.78,.22]],[[.5,1],[.22,.78]]],'ᛈ':[[[.72,0],[.3,.2],[.3,.8],[.72,1]],[[.3,.2],[.55,.35]],[[.3,.8],[.55,.65]]],
 'ᛉ':[[[.5,0],[.5,1]],[[.2,.05],[.5,.38],[.8,.05]]],'ᛊ':[[[.72,0],[.3,.35],[.72,.65],[.3,1]]],'ᛏ':[[[.5,0],[.5,1]],[[.2,.3],[.5,0],[.8,.3]]],
 'ᛒ':[[[.3,0],[.3,1],[.72,.75],[.3,.5],[.72,.25],[.3,0]]],'ᛖ':[[[.25,1],[.25,0],[.5,.3],[.75,0],[.75,1]]],
 'ᛗ':[[[.25,1],[.25,0],[.75,.45]],[[.75,1],[.75,0],[.25,.45]]],'ᛚ':[[[.3,1],[.3,0],[.72,.3]]],'ᛜ':[[[.5,.2],[.8,.5],[.5,.8],[.2,.5],[.5,.2]]],
 'ᛞ':[[[.2,0],[.2,1],[.8,0],[.8,1],[.2,0]]],'ᛟ':[[[.2,1],[.72,.45],[.5,.12],[.28,.45],[.8,1]]]
};
const RUNES=Object.keys(FUTHARK);
function runeFor(name){let h=0;for(const c of String(name||''))h=(h*31+c.charCodeAt(0))>>>0;return RUNES[h%RUNES.length]}
function drawRune(ch,x,y,size,col,a,glowA){
  const strokes=FUTHARK[ch]; if(!strokes)return;
  const path=()=>{hx.beginPath();for(const s of strokes){s.forEach(([u,v],i)=>{const px=x+(u-0.5)*size*0.6,py=y+(v-0.5)*size;i?hx.lineTo(px,py):hx.moveTo(px,py)})}};
  if(glowA){hx.strokeStyle=`rgba(${col},${(a*glowA).toFixed(3)})`;hx.lineWidth=4;path();hx.stroke()}
  hx.strokeStyle=`rgba(${col},${a.toFixed(3)})`;hx.lineWidth=1;path();hx.stroke();
}

/* ------------------------------------ render ------------------------------------ */
let W=900,Hh=700,DPR=1,CX=450,CY=345,ORBR=290,SCALE=118,yaw=0,pitch=0.17;
function resize(){
  DPR=Math.min(window.devicePixelRatio||1,2);
  W=cv.clientWidth;Hh=cv.clientHeight;
  for(const c of [cv,hud]){c.width=Math.round(W*DPR);c.height=Math.round(Hh*DPR)}
  hx.setTransform(DPR,0,0,DPR,0,0);
  gl.viewport(0,0,cv.width,cv.height);
  CX=W/2;CY=Hh*0.5;ORBR=Math.min(W,Hh)*0.43;SCALE=ORBR*0.36;
}
window.addEventListener('resize',resize);new ResizeObserver(resize).observe(HOST);resize();

function project(p){
  const ca=Math.cos(pitch),sa=Math.sin(pitch);let x=p[0],y=p[1]*ca-p[2]*sa,z=p[1]*sa+p[2]*ca;
  const cb=Math.cos(yaw),sb=Math.sin(yaw);const x2=x*cb+z*sb,z2=-x*sb+z*cb;const k=6/(6-z2);
  return [CX+x2*SCALE*k,FACEY()+y*SCALE*k,z2,k];
}
const FACEY=()=>CY-0.10*SCALE;   // desceu um pouco: os espetos do cabelo sobem ate ~2,6 unidades   // o rosto emerge levemente acima do centro; a barba desce e se dissolve na orb

function setCommon(p,t){
  const u=p.u;
  gl.uniform2f(u.u_center,CX,FACEY());gl.uniform2f(u.u_res,W,Hh);gl.uniform1f(u.u_scale,SCALE);gl.uniform1f(u.u_orbR,ORBR);gl.uniform1f(u.u_dpr,DPR);
  gl.uniform1f(u.u_yaw,yaw);gl.uniform1f(u.u_pitch,pitch);gl.uniform1f(u.u_time,t);
  gl.uniform1fv(u.u_sharp,sharp.map(v=>v*v*(3-2*v)));gl.uniform1fv(u.u_glow,glow);
  gl.uniform2f(u.u_eye,EC[0],EC[1]);gl.uniform1f(u.u_eyeLit,V.eyeLit);gl.uniform1f(u.u_ring,V.ring);
  gl.uniform1f(u.u_mouth,V.mouth);gl.uniform1f(u.u_talk,V.talk);gl.uniform1f(u.u_think,V.think);gl.uniform1f(u.u_breath,V.breath);
  gl.uniform3f(u.u_pulseCol,...pulseCol);gl.uniform1f(u.u_pulse,V.pulse);
  gl.uniform1f(u.u_val,E.val);gl.uniform1f(u.u_ativ,E.ativ);gl.uniform1f(u.u_caos,E.caos);gl.uniform1f(u.u_fogo,E.fogo);gl.uniform1f(u.u_blink,E.blink);gl.uniform1f(u.u_roll,E.roll);gl.uniform1f(u.u_surp,E.surp);
  gl.uniform3f(u.u_hairA,...HAIR.a);gl.uniform3f(u.u_hairB,...HAIR.b);
}

let last=performance.now(), T=0, frames=0, ftAcc=0;
function frame(now){
  const dt=Math.min(0.05,(now-last)/1000); last=now; T+=dt;
  const t0=performance.now();
  const m=st.modo, ms=now-modeStart;
  st.pensando_ms=m==='pensando'?Math.round(ms):0;

  // áudio simulado: o slider é o teto, a sílaba é o ritmo
  const syl=0.45+0.55*Math.abs(Math.sin(T*8.7)*Math.sin(T*3.3+1.1));
  const aud=(m==='ouvindo'||m==='falando')?st.nivel_audio*syl:0;

  // sintonia -> nitidez por região (transição de ~2 s)
  REG.forEach((id,i)=>{const tg=st.sintonia>=thr[id]?1:0;sharp[i]=instant?tg:(sharp[i]<tg?Math.min(tg,sharp[i]+dt/2):Math.max(tg,sharp[i]-dt/2))});

  const breath=0.5+0.5*Math.sin(T*0.6*1.6);
  V.breath=breath;
  const eyeT={idle:0.32+0.08*breath,ouvindo:0.75+0.6*aud,pensando:0.5,falando:0.55,mao:0.6,fiscal_ok:0.9,fiscal_falha:0.9}[m]??0.35;
  V.eyeLit=instant?eyeT:sm1(V.eyeLit,eyeT,m==='ouvindo'?10:3,dt);
  // Regra da Forja: nada em loop além da respiração da orb. O anel só gira quando há estado (ouvindo/pensando/mão);
  // o relógio do rosto (poeira, ondulação) só anda fora do repouso. Em idle a cabeça fica travada.
  V.ringSpeed=sm1(V.ringSpeed,{ouvindo:0.9+2.2*aud,pensando:0.35+Math.min(1,ms/8000)*0.8,mao:0.5}[m]??0,instant?60:3,dt);
  V.faceT=(V.faceT||0)+dt;
  V.ring+=V.ringSpeed*dt;
  V.mouth=sm1(V.mouth,m==='falando'?aud:0,m==='falando'?14:5,dt);
  V.talk=sm1(V.talk,m==='falando'?1:0,4,dt);
  const thinkT=m==='pensando'?Math.min(1,0.25+ms/3000*0.5+Math.max(0,ms-3000)/5000*0.25):0;
  V.think=instant&&m==='pensando'?thinkT:sm1(V.think,thinkT,2,dt);
  V.eyeRunes=sm1(V.eyeRunes,m==='pensando'&&ms>3000?1:0,instant?60:2,dt);
  V.radio=sm1(V.radio,m==='pensando'&&ms>8000?1:0,1.2,dt);
  V.maoA=sm1(V.maoA,m==='mao'?1:0,instant?60:2.5,dt);
  let pe=(now-pulseStart)/1000; if(Q.has('hold')&&(m==='fiscal_ok'||m==='fiscal_falha')) pe=+Q.get('hold'); // só para captura de teste
  V.pulse=pe>=0&&pe<1?Math.sin(Math.PI*pe):0;
  if(m==='fiscal_ok'&&pe>1.25&&!instant) setModo('idle');
  if(m==='fiscal_falha'&&pe>2.6&&!instant) pulseStart=now;   // falha fica pulsando ate o proximo estado
  const lvlT=m==='falando'?aud*0.45:m==='ouvindo'?aud*0.3:0.04+0.04*breath;
  V.level=sm1(V.level,lvlT,lvlT>V.level?16:3.2,dt);
  glow.fill(0);
  glow[RI['boca']]=V.talk*V.mouth*1.2; glow[RI['barba-alta']]=V.talk*V.mouth*0.7; glow[RI['barba-baixa']]=V.talk*V.mouth*0.35;
  glow[RI['testa']]=V.think*0.25;
  // ---- VIDA: emocao suave + eventos que decaem + corpo sempre em movimento ----
  const cl=(v,a,b)=>Math.max(a,Math.min(b,v));
  E.evVal*=Math.exp(-dt/6); E.evAtiv*=Math.exp(-dt/8); E.evFogo*=Math.exp(-dt/5); E.evSurp*=Math.exp(-dt/1.2);
  E.val=sm1(E.val,cl(st.valencia+E.evVal,-1,1),1.2,dt);
  E.ativ=sm1(E.ativ,cl(st.ativacao+(m==='idle'?0:0.25)+(m==='falando'?0.15*aud:0)+E.evAtiv,0,1),1.5,dt);
  E.caos=sm1(E.caos,cl(st.caos,0,1),0.8,dt);
  E.fogo=sm1(E.fogo,cl(st.fogo+Math.max(0,st.caos-0.8)*5+E.evFogo,0,1),2,dt);
  E.surp=sm1(E.surp,cl(E.evSurp,0,1),4,dt);
  if(T>E.nextBlink){E.blinkT=T;E.nextBlink=T+(1.8+4*Math.random())*(1-0.5*E.ativ)}
  const bt=(T-E.blinkT)/0.15; E.blink=bt>=0&&bt<1?Math.sin(Math.PI*bt):0;
  // Idle sway alone did not match the voice or the mood, so drift is now small;
  // o grosso do movimento vem da VOZ (batida: silaba forte = aceno curto, alterna o lado) e do gesto pedido.
  const vox=st.nivel_audio||0;
  if(m==='falando'&&vox-E.prevVox>0.10&&T-E.beatT>0.32){E.beatT=T;E.beatSide=-E.beatSide;E.nod+=0.035+0.05*E.ativ+0.04*vox;E.turn+=E.beatSide*(0.02+0.03*E.ativ)}
  if(m==='ouvindo'){   // "uhum": the user spoke for ~1 s and stopped -> aceno curto e leve
    if(vox>0.15){E.falaT+=dt;E.quietT=0}
    else if((E.quietT=(E.quietT||0)+dt)>0.4){if(E.falaT>0.9&&!G.tipo){G.tipo='sim';G.t0=now;G.dur=0.9;G.amp=0.45} E.falaT=0}
  }
  E.prevVox=sm1(E.prevVox,vox,12,dt);
  E.nod*=Math.exp(-dt/0.16); E.turn*=Math.exp(-dt/0.5);
  let gy=0,gp=0,gr=0;
  if(G.tipo){const gt=(now-G.t0)/1000; if(gt>G.dur)G.tipo=null; else{const env=Math.sin(Math.PI*gt/G.dur)*G.amp;
    if(G.tipo==='sim')gp=0.18*env*Math.sin(2*Math.PI*2.3*gt);
    else if(G.tipo==='nao')gy=0.28*env*Math.sin(2*Math.PI*2.0*gt);
    else if(G.tipo==='balancar'){gr=0.18*env*Math.sin(2*Math.PI*0.9*gt);gy=0.06*env*Math.sin(2*Math.PI*0.9*gt+1.2)}
    else if(G.tipo==='inclinar')gr=0.2*env}}
  const calmo=m==='idle'?0.45:1;
  yaw=calmo*(0.05*Math.sin(T*0.31)+0.025*Math.sin(T*0.83+1.3))+(0.03+0.06*E.caos)*Math.sin(T*1.9)*E.ativ*calmo+(m==='ouvindo'?0.07:0)+E.turn+gy;
  pitch=0.17+0.025*Math.sin(T*0.47+0.7)+(m==='falando'?0.04*V.mouth:0)+(m==='pensando'?-0.06:0)+0.02*breath+E.nod+gp;
  E.roll=calmo*0.03*Math.sin(T*0.23)+(m==='ouvindo'?0.08:0)+0.05*E.caos*Math.sin(T*2.3)+(m==='pensando'?0.05*Math.sin(T*0.5):0)-0.04*Math.max(0,-E.val)+0.5*E.turn+gr;

  /* --- GL --- */
  gl.disable(gl.DEPTH_TEST);
  gl.disable(gl.BLEND);
  gl.useProgram(P_BG);bindAttrs(P_BG,B_BG,[['a_pos',2,0]],2);
  gl.uniform2f(P_BG.u.u_res,W,Hh);gl.uniform2f(P_BG.u.u_c,CX,CY);gl.uniform1f(P_BG.u.u_level,V.level);gl.uniform1f(P_BG.u.u_breath,breath);
  gl.drawArrays(gl.TRIANGLES,0,3);

  gl.enable(gl.BLEND);gl.blendFunc(gl.ONE,gl.ONE);
  gl.useProgram(P_ORB);bindAttrs(P_ORB,B_ORB,[['a_ring',1,0],['a_index',1,1],['a_count',1,2]],3);
  const ou=P_ORB.u;
  gl.uniform1f(ou.u_time,T);gl.uniform1f(ou.u_level,V.level);gl.uniform1f(ou.u_bass,V.level*0.6);gl.uniform1f(ou.u_treble,V.level*0.4);
  gl.uniform1f(ou.u_base,ORBR*0.93);gl.uniform2f(ou.u_center,CX,CY);gl.uniform2f(ou.u_res,W,Hh);gl.uniform1f(ou.u_dpr,DPR);
  gl.uniform1f(ou.u_radio,V.radio);gl.uniform1f(ou.u_hole,0.10);gl.uniform1f(ou.u_gain,0.72);
  gl.drawArrays(gl.POINTS,0,N_ORB);

  gl.useProgram(P_LINE);bindAttrs(P_LINE,B_LINE,[['a_pos',3,0],['a_meta',4,3]],7);setCommon(P_LINE,V.faceT);
  gl.drawArrays(gl.LINES,0,N_LINE);
  gl.useProgram(P_PT);bindAttrs(P_PT,B_PT,[['a_pos',3,0],['a_meta',4,3]],7);setCommon(P_PT,V.faceT);
  gl.drawArrays(gl.POINTS,0,N_PT);

  /* --- HUD 2D: runas e pulsos --- */
  hx.clearRect(0,0,W,Hh);
  hx.globalCompositeOperation='lighter';
  if(V.maoA>0.01){
    const n=16;
    for(let i=0;i<n;i++){
      const a=i/n*TAU+T*0.35, p=project([Math.cos(a)*2.05,-0.25+0.10*Math.sin(a*2+T*0.5),Math.sin(a)*2.05]);
      const front=p[2]>0?1:0.35;
      drawRune(RUNES[(i*5)%RUNES.length],p[0],p[1],15*p[3],'179,136,255',V.maoA*0.75*front,front>0.5?0.18:0);
    }
    const f=project([0,-0.98,H(0,-0.98)+0.02]);
    drawRune(runeFor(st.mao),f[0],f[1],22*f[3],'214,196,255',V.maoA*(0.75+0.25*Math.sin(T*2.2)),0.3);
  }
  if(V.eyeRunes>0.01){
    for(let i=0;i<8;i++){
      const a=i/8*TAU-T*0.6, p=project([EC[0]+Math.cos(a)*0.2,EC[1]+Math.sin(a)*0.2,H(EC[0],EC[1])+0.08]);
      drawRune(RUNES[(i*3+2)%RUNES.length],p[0],p[1],9,'0,229,255',V.eyeRunes*0.55,0);
    }
  }
  if(V.pulse>0.001){
    const e=project([EC[0],EC[1],H(EC[0],EC[1])+0.08]), pe2=Math.min(1,pe);
    const c=pulseCol.map(v=>Math.round(v*255)).join(',');
    hx.strokeStyle=`rgba(${c},${(0.55*(1-pe2)).toFixed(3)})`;hx.lineWidth=1;
    hx.beginPath();hx.arc(e[0],e[1],8+pe2*ORBR*1.05,0,TAU);hx.stroke();
    hx.strokeStyle=`rgba(${c},${(0.25*(1-pe2)).toFixed(3)})`;
    hx.beginPath();hx.arc(e[0],e[1],4+pe2*ORBR*0.7,0,TAU);hx.stroke();
  }
  hx.globalCompositeOperation='source-over';

  ftAcc+=performance.now()-t0;frames++;
  if(frames%30===0){window.__cpuFrameMs=ftAcc/30;ftAcc=0}
  if(VIVO.on) { if(st.modo==='idle') setTimeout(()=>requestAnimationFrame(frame),45); else requestAnimationFrame(frame); }
}
setModo(st.modo);
requestAnimationFrame(frame);
return {
  set(o){ return window.odinAvatar.set(o); },
  parar(){ VIVO.on=false; },
};
}
