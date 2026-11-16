document.querySelectorAll('[data-showcase-gallery]').forEach(gallery => {
  const slides = [...gallery.querySelectorAll('[data-showcase-slide]')];
  const buttons = [...gallery.parentElement.querySelectorAll('[data-showcase-thumb]')];
  buttons.forEach(button => button.addEventListener('click', () => {
    const selected = Number(button.dataset.showcaseThumb);
    if (!Number.isInteger(selected) || selected < 0 || selected >= slides.length) return;
    slides.forEach((slide, index) => {
      if (index !== selected) slide.querySelector('video')?.pause();
      slide.hidden = index !== selected;
    });
    buttons.forEach((item, index) => {
      item.classList.toggle('active', index === selected);
      item.setAttribute('aria-pressed', String(index === selected));
    });
  }));
});
