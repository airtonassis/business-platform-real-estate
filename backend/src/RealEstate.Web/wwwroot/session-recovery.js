// Keep the original DOM (including native FileList) while the existing OIDC flow runs in another tab.
export function offerSessionRecovery(target){
    const link=document.createElement('a');
    link.href='/account/login';link.target='_blank';link.rel='noopener';
    link.className='button secondary';link.textContent='Retomar acesso em outra aba';
    link.dataset.sessionRecovery='';
    link.addEventListener('click',()=>{document.querySelector('#property-form').dataset.recoverSession='true';});
    target.append(document.createTextNode(' '),link,document.createTextNode(' Após entrar, confira a mesma identidade e filial, volte a esta aba e tente novamente.'));
}

export async function prepareSessionRecovery(form){
    if(form.dataset.recoverSession!=='true')return;
    const response=await fetch(form.action,{credentials:'same-origin',cache:'no-store'});
    const doc=new DOMParser().parseFromString(await response.text(),'text/html');
    const fresh=doc.querySelector('#property-form');
    function reject(message){throw Object.assign(Error(message),{sessionRecovery:true,authenticationRequired:true});}
    if(!response.ok||!fresh?.dataset.sessionSubject)reject('Retome seu acesso na outra aba antes de tentar novamente. Seus dados e arquivos continuam nesta tela.');
    for(const key of ['sessionSubject','sessionOrganization','sessionEstablishment'])
        if(!form.dataset[key]||form.dataset[key]!==fresh.dataset[key])reject('A identidade ou filial mudou. Retorne ao mesmo acesso e à mesma filial antes de salvar este cadastro. Seus dados e arquivos continuam nesta tela.');
    const token=fresh.querySelector('[name=__RequestVerificationToken]')?.value;
    if(!token)reject('Não foi possível validar a retomada do acesso. Seus dados e arquivos continuam nesta tela.');
    // Refresh only CSRF, never replace filled values, FileList, UUID or optimistic version.
    for(const field of document.querySelectorAll('[name=__RequestVerificationToken]'))field.value=token;
    delete form.dataset.recoverSession;
}
