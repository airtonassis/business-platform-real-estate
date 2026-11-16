// Optional assistance fills only untouched, blank fields. Manual data always wins.
export function automaticPostalFields(current,suggested,edited){
    return Object.fromEntries(Object.entries(suggested).filter(([key])=>!String(current[key]??'').trim()&&!edited.has(key)));
}
export function createPostalLookup({fetcher=globalThis.fetch,timeoutMs=5000}={}) {
    let generation=0,active;
    function invalidate(){generation++;active?.abort();}
    async function search(value){
        invalidate();const ticket=generation;
        if(!/^\d{5}-?\d{3}$/.test(value))return {state:'manual'};
        const cep=value.replace('-','');const controller=new AbortController();active=controller;
        let timer;
        try {
            const result=await Promise.race([
                (async()=>{
                    const response=await fetcher(`https://viacep.com.br/ws/${cep}/json/`,{signal:controller.signal,credentials:'omit',referrerPolicy:'no-referrer',redirect:'error'});
                    if(!response.ok)throw Error('HTTP failure');
                    return await response.json();
                })(),
                new Promise((_,reject)=>{timer=setTimeout(()=>{controller.abort();reject(Error('Timeout'));},timeoutMs);})
            ]);
            if(ticket!==generation)return {state:'stale'};
            if(result?.erro===true||result?.erro==='true')return {state:'not-found'};
            if(!result||typeof result!=='object'||Array.isArray(result)||typeof result.cep!=='string'||result.cep.replace('-','')!==cep)throw Error('Invalid response');
            const fields={};
            for(const [key,source,max] of [['Street','logradouro',300],['Neighborhood','bairro',200],['Municipality','localidade',200],['State','uf',2],['MunicipalityIbgeCode','ibge',7]]){
                const text=typeof result[source]==='string'?result[source].trim():'';
                if(!text||text.length>max)continue;
                if(key==='State'&&!/^(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)$/.test(text))continue;
                if(key==='MunicipalityIbgeCode'&&!/^\d{7}$/.test(text))continue;
                fields[key]=text;
            }
            return Object.keys(fields).length?{state:'suggestion',fields}:{state:'not-found'};
        }catch{return {state:ticket===generation?'unavailable':'stale'};}
        finally{clearTimeout(timer);}
    }
    return {search,invalidate};
}

export function manualPoint(raw){
    let text=String(raw??'').trim();
    if(text.startsWith('https://')){
        let url;try{url=new URL(text);}catch{return null;}
        if(!['www.google.com','maps.google.com','google.com'].includes(url.hostname))return null;
        text=url.searchParams.get('query')??url.searchParams.get('q')??url.pathname.match(/@(-?\d+(?:\.\d+)?,-?\d+(?:\.\d+)?)/)?.[1]??'';
    }
    const pair=text.match(/^\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*$/);if(!pair)return null;
    const latitude=Number(pair[1]),longitude=Number(pair[2]);
    return Math.abs(latitude)<=90&&Math.abs(longitude)<=180?{Latitude:String(latitude),Longitude:String(longitude)}:null;
}
export function mapsUrl(address){
    const text=key=>String(address[key]??'').trim();
    let query;
    const lat=text('Latitude'),lon=text('Longitude');
    if(lat&&lon&&Number.isFinite(Number(lat))&&Number.isFinite(Number(lon))&&Math.abs(Number(lat))<=90&&Math.abs(Number(lon))<=180)
        query=`${Number(lat)},${Number(lon)}`;
    else {
        const parts=['Street','Number','Complement','Neighborhood','Municipality','State','PostalCode','Country'].map(text);
        if(address.WithoutNumber===true||address.WithoutNumber==='true')parts[1]='S/N';
        // S/N, country or a complement alone is insufficient to locate an address.
        if(!['Street','Municipality','PostalCode','LegacyText'].some(k=>text(k)))return null;
        query=parts.filter(Boolean).join(', ');
        if(!['Street','Municipality','PostalCode'].some(k=>text(k)))query=text('LegacyText');
    }
    return 'https://www.google.com/maps/search/?api=1&query='+encodeURIComponent(query);
}

export function coordinateState(address){
    const latitude=String(address.Latitude??'').trim(),longitude=String(address.Longitude??'').trim();
    if(!latitude&&!longitude)return 'empty';
    if(!latitude||!longitude)return 'partial';
    const lat=Number(latitude),lon=Number(longitude);
    return Number.isFinite(lat)&&Number.isFinite(lon)&&Math.abs(lat)<=90&&Math.abs(lon)<=180?'ready':'invalid';
}

function initialize(form){
    const fields=Object.fromEntries([...form.querySelectorAll('[name^="Input.Address."]')].map(e=>[e.name.slice('Input.Address.'.length),e]));
    const postal=fields.PostalCode,status=form.querySelector('#cep-status'),retry=form.querySelector('#cep-retry');
    const map=form.querySelector('#address-map'),mapHint=form.querySelector('#map-hint');
    const lookup=createPostalLookup();let revision=0;const edited=new Set();
    const values=()=>Object.fromEntries(Object.entries(fields).map(([k,e])=>[k,e.value]));
    let observed=JSON.stringify(values());
    function updateMap(){
        const current=values(),state=coordinateState(current),url=mapsUrl(current);
        map.hidden=!url;if(url)map.href=url;else map.removeAttribute('href');mapHint.hidden=!!url;
        map.textContent=state==='ready'?'Ver coordenadas no Google Maps (nova aba)':'Ver endereço no Google Maps (nova aba)';
        const status=form.querySelector('#location-coordinate-status');
        if(!status)return;
        status.dataset.state=state;
        status.textContent=state==='ready'?`Ponto informado: ${current.Latitude}, ${current.Longitude}.`
            :state==='partial'?'Complete latitude e longitude ou deixe ambos vazios.'
            :state==='invalid'?'Confira os limites: latitude entre −90 e 90; longitude entre −180 e 180.'
            :'Coordenadas não informadas. O rascunho pode ser salvo sem um ponto.';
    }
    form.querySelector('[data-import-point]')?.addEventListener('click',()=>{
        const point=manualPoint(form.querySelector('#manual-point').value);
        if(!point){form.querySelector('#location-coordinate-status').textContent='Informe coordenadas validas ou um link completo que contenha o ponto. Nenhum servico externo foi consultado.';return;}
        for(const [key,value] of Object.entries(point)){fields[key].value=value;fields[key].dispatchEvent(new Event('input',{bubbles:true}));}updateMap();
    });
    form.querySelector('[data-confirm-point]')?.addEventListener('click',()=>{
        if(coordinateState(values())!=='ready'){updateMap();return;}
        fields.CoordinateSource.value='Manual confirmado';fields.CoordinateSource.dispatchEvent(new Event('input',{bubbles:true}));
        form.querySelector('#location-coordinate-status').textContent='Ponto confirmado. Salve o rascunho para manter a localizacao.';
    });
    function dismiss(message=''){
        revision++;lookup.invalidate();status.textContent=message;retry.hidden=true;
    }
    async function search(){
        if(postal.matches(':disabled'))return;
        dismiss('');
        if(!/^\d{5}-?\d{3}$/.test(postal.value))return;
        const current=revision,snapshot=JSON.stringify(values());status.textContent='Consultando CEP…';
        const result=await lookup.search(postal.value);
        if(current!==revision||snapshot!==JSON.stringify(values())||result.state==='stale')return;
        if(result.state==='suggestion'){
            const applicable=automaticPostalFields(values(),result.fields,edited);
            for(const [key,value] of Object.entries(applicable))fields[key].value=value;
            observed=JSON.stringify(values());updateMap();
            status.textContent='';retry.hidden=true;
        }else {
            status.textContent=result.state==='not-found'?'CEP não encontrado. Preencha o endereço manualmente.':'Consulta indisponível. Preencha manualmente ou tente novamente.';
            retry.hidden=false;
        }
    }
    const numberChanged=()=>{
        const value=fields.Number.value.trim();
        fields.WithoutNumber.value=/^s\/n$/i.test(value)?'true':value?'false':'';
    };
    fields.Number.addEventListener('input',numberChanged);
    fields.Number.addEventListener('change',numberChanged);
    for(const [key,field] of Object.entries(fields)){
        const changed=()=>{const now=JSON.stringify(values());if(now===observed)return;observed=now;edited.add(key);dismiss();updateMap();if(key==='PostalCode')void search();};
        field.addEventListener('input',changed);field.addEventListener('change',changed);
    }
    retry.addEventListener('click',()=>void search());updateMap();
}
if(typeof document!=='undefined'){
    const form=document.querySelector('#property-form');if(form)initialize(form);
}
