'use strict';
// Presentation only. No role selection, identity storage or authorization decisions.
(() => {
  const sidebar = document.getElementById('sidebar');
  const open = document.getElementById('open-menu');
  const close = document.getElementById('close-menu');
  const backdrop = document.getElementById('menu-backdrop');
  globalThis.AraIa = {
    clearContext() {
      for (const id of ['header-company', 'sidebar-company']) {
        const node = document.getElementById(id); if (node) node.textContent = 'Acesso indisponível';
      }
      for (const id of ['header-name', 'header-role']) {
        const node = document.getElementById(id); if (node) node.textContent = 'Valide seu acesso';
      }
      const avatar = document.getElementById('header-avatar'); if (avatar) avatar.textContent = 'A';
      document.getElementById('header-user-group')?.setAttribute('aria-label', 'Acesso indisponível');
      document.querySelectorAll('.enterprise-navigation, .manager-navigation').forEach(node => { node.hidden = true; });
    }
  };
  document.querySelector('.skip-link')?.addEventListener('click', event => {
    event.preventDefault(); document.getElementById('main')?.focus();
  });
  document.querySelectorAll('a[data-user-detail]').forEach(link => link.addEventListener('click', event => {
    if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const params = new URL(link.href).searchParams;
    const item = params.get('registration') || params.get('item');
    const prefix = 'person-desktop-';
    history.replaceState(history.state, '', location.pathname + location.search + '#' + prefix + item);
  }));
  const focusTarget = document.getElementById(location.hash.slice(1));
  if (focusTarget?.matches('a[data-user-detail]')) focusTarget.focus();
  const detail = document.getElementById('member-details');
  if (detail) {
    detail.removeAttribute('open'); detail.showModal();
    detail.querySelector('.detail-close').focus();
    function closeDetail(event) {
      event.preventDefault();
      const previous = document.referrer ? new URL(document.referrer) : null;
      if (previous?.origin === location.origin && previous.pathname === '/app' && previous.searchParams.get('section') === 'members' && !['item', 'registration', 'new'].some(key => previous.searchParams.has(key))) history.back();
      else {
        const target = new URL(detail.querySelector('.detail-close').href);
        const prefix = 'person-desktop-';
        const params = new URL(location.href).searchParams;
        const item = params.get('registration') || params.get('item');
        if (item) target.hash = prefix + item;
        location.replace(target.href);
      }
    }
    detail.addEventListener('cancel', closeDetail);
    detail.querySelector('.detail-close').addEventListener('click', closeDetail);
  }
  document.querySelector('.session-utility')?.addEventListener('keydown', event => {
    if (event.key === 'Escape') { event.currentTarget.open = false; event.currentTarget.querySelector('summary').focus(); }
  });
  if (!sidebar || !open) return;
  function collapse(restore = false) {
    sidebar.classList.remove('open'); backdrop.hidden = true; open.setAttribute('aria-expanded', 'false');
    sidebar.inert = matchMedia('(max-width:800px)').matches;
    if (restore) open.focus();
  }
  open.addEventListener('click', () => {
    sidebar.inert = false; sidebar.classList.add('open'); backdrop.hidden = false;
    open.setAttribute('aria-expanded', 'true'); close.focus();
  });
  close.addEventListener('click', () => collapse(true)); backdrop.addEventListener('click', () => collapse(true));
  document.addEventListener('keydown', event => {
    if (!sidebar.classList.contains('open')) return;
    if (event.key === 'Escape') collapse(true);
    if (event.key === 'Tab') {
      const links = [...sidebar.querySelectorAll('a[href],button,summary')].filter(node => node.getClientRects().length);
      const first = links[0], last = links.at(-1);
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }
  });
  addEventListener('resize', () => collapse()); collapse();
})();
